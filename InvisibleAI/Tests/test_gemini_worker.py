"""Gemini contract/privacy tests use synthetic credentials and never contact Google."""

import base64
import importlib
import importlib.util
import inspect
import io
import json
import subprocess
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from gemini_webapi.constants import AccountStatus
from gemini_webapi.exceptions import AuthError, ModelInvalidError

ROOT = Path(__file__).resolve().parents[1]
WORKER = ROOT / "WindowsCompanion/AI/GeminiWeb/worker.py"
spec = importlib.util.spec_from_file_location("worker", WORKER)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)

SENTINEL = "synthetic-cookie-sentinel"


class Client:
    def __init__(self, sid, ts, proxy=None):
        self.sid = sid
        self.ts = ts
        self.account_status = AccountStatus.AVAILABLE
        self.closed = False
        self.arguments = None
        self.init_arguments = None

    async def init(self, **kwargs):
        self.init_arguments = kwargs

    def list_models(self):
        return [SimpleNamespace(model_id="gemini-account", display_name="Account model", is_available=True),
                SimpleNamespace(model_id="unavailable", display_name="Unavailable", is_available=False)]

    def resolve_model(self, model):
        return model

    async def generate_content(self, prompt, **kwargs):
        self.arguments = kwargs
        if kwargs["files"]:
            self.assert_image = kwargs["files"][0].getvalue()
            kwargs["files"][0].close()  # Real GeminiClient closes BytesIO after generation.
        return SimpleNamespace(text="O(log n)")

    async def close(self):
        self.closed = True


class WorkerTests(unittest.IsolatedAsyncioTestCase):
    def request(self, **extra):
        return {"cookies": {"__Secure-1PSID": SENTINEL}, "operation": "models", **extra}

    def factory(self, *args, **kwargs):
        self.client = Client(*args, **kwargs)
        return self.client

    async def test_catalog_filters_unavailable_and_hides_cookies(self):
        result = await worker.execute(self.request(), self.factory)
        self.assertEqual(result, {"models": [{"id": "gemini-account", "name": "Account model"}]})
        self.assertNotIn(SENTINEL, json.dumps(result))
        self.assertTrue(self.client.closed)
        self.assertFalse(self.client.init_arguments["auto_refresh"])
        self.assertFalse(self.client.init_arguments["verbose"])

    async def test_image_messages_in_memory_and_temporary_chat(self):
        png = b"\x89PNG\r\n\x1a\nsynthetic"
        request = self.request(operation="generate", model="gemini-account", imageBase64=base64.b64encode(png).decode(),
                               messages=[{"role": "system", "content": "concise"}, {"role": "user", "content": "question"}])
        result = await worker.execute(request, self.factory)
        self.assertEqual(set(result), {"content", "provider", "model", "usage", "finishReason"})
        self.assertEqual(result["content"], "O(log n)")
        self.assertTrue(self.client.arguments["temporary"])
        self.assertEqual(self.client.assert_image, png)
        self.assertTrue(self.client.arguments["files"][0].closed)

    async def test_missing_cookies_no_client_started(self):
        with self.assertRaises(AuthError):
            await worker.execute({"cookies": {}}, lambda *a, **k: self.fail("Client started without cookie"))

    async def test_guest_session_rejected_and_closed(self):
        def guest(*a, **k):
            client = self.factory(*a, **k)
            client.account_status = None
            return client
        with self.assertRaises(AuthError):
            await worker.execute(self.request(), guest)
        self.assertTrue(self.client.closed)

    async def test_unavailable_model_and_malformed_cookie(self):
        with self.assertRaises(ModelInvalidError):
            await worker.execute(self.request(operation="generate", model="unavailable"), self.factory)
        with self.assertRaises(ValueError):
            await worker.execute(self.request(cookies={"__Secure-1PSID": "newline\ninjection"}), self.factory)

    async def test_image_buffer_closes_even_on_upstream_failure(self):
        async def fail(prompt, **kwargs):
            self.image = kwargs["files"][0]
            raise AuthError(SENTINEL)
        def factory(*a, **k):
            client = self.factory(*a, **k)
            client.generate_content = fail
            return client
        with self.assertRaises(AuthError):
            await worker.execute(self.request(operation="generate", model="gemini-account",
                imageBase64=base64.b64encode(b"\x89PNG\r\n\x1a\n").decode(),
                messages=[{"content": "rules"}, {"content": "question"}]), factory)
        self.assertTrue(self.image.closed)
        self.assertTrue(self.client.closed)

    def test_pinned_library_contract_and_cache_logging_hardening(self):
        client = worker.harden_library()
        self.assertIn("temporary", inspect.signature(client.generate_content).parameters)
        self.assertIn("auto_refresh", inspect.signature(client.init).parameters)
        module = importlib.import_module("gemini_webapi.client")
        token = importlib.import_module("gemini_webapi.utils.get_access_token")
        rotate = importlib.import_module("gemini_webapi.utils.rotate_1psidts")
        with patch.object(Path, "write_text", side_effect=AssertionError("Raw cookie write")):
            self.assertIsNone(module.save_cookies({"__Secure-1PSID": SENTINEL}))
            self.assertIsNone(module.clear_cookies_cache({"__Secure-1PSID": SENTINEL}))
            self.assertIsNone(token._get_cookies_cache_path(None))
            self.assertIsNone(rotate._get_cookies_cache_path(None))
            self.assertEqual(token.load_browser_cookies(), {})
        from loguru import logger
        captured = io.StringIO()
        with patch("sys.stderr", captured):
            logger.error(SENTINEL)
        self.assertEqual(captured.getvalue(), "")

    def test_errors_never_contain_upstream_authentication_text(self):
        for error, code in [(AuthError(SENTINEL), "auth"), (TimeoutError(SENTINEL), "timeout"),
                            (ValueError(SENTINEL), "format"), (RuntimeError(SENTINEL), "unavailable")]:
            self.assertEqual(worker.error_code(error), code)
            self.assertNotIn(SENTINEL, worker.error_code(error))
        for status, code in [(401, "auth"), (403, "auth"), (429, "rate_limit")]:
            error = RuntimeError(SENTINEL)
            error.response = SimpleNamespace(status_code=status)
            self.assertEqual(worker.error_code(error), code)

    def test_private_process_sanitizes_malformed_requests(self):
        result = subprocess.run([sys.executable, "-I", "-B", str(WORKER)], input=json.dumps({"cookies": {}}),  # noqa: S603 - trusted interpreter and repository fixture
                                capture_output=True, text=True, timeout=20, check=True)
        self.assertEqual(json.loads(result.stdout), {"errorCode": "auth"})
        self.assertEqual(result.stderr, "")
        result = subprocess.run([sys.executable, "-I", "-B", str(WORKER)], input=SENTINEL,  # noqa: S603 - trusted interpreter and repository fixture
                                capture_output=True, text=True, timeout=20, check=True)
        self.assertEqual(json.loads(result.stdout), {"errorCode": "format"})
        self.assertNotIn(SENTINEL, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
