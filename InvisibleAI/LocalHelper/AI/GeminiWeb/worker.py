"""Private one-request Gemini Web2API worker. JSON stdin/stdout; no HTTP listener or secret files."""

import asyncio
import base64
import contextlib
import importlib
import io
import json
import os
import sys

MAX_INPUT = 8 * 1024 * 1024


class ImageBuffer(io.BytesIO):
    """The upstream client closes uploads itself; wipe on that close as well as ours."""

    def close(self):
        if not self.closed:
            view = self.getbuffer()
            view[:] = b"\0" * view.nbytes
            view.release()
        super().close()


def harden_library():
    """Disable optional browser discovery, disk cookie caches, and all library logging."""
    from loguru import logger

    logger.remove()
    module = importlib.import_module("gemini_webapi.client")
    token = importlib.import_module("gemini_webapi.utils.get_access_token")
    rotate = importlib.import_module("gemini_webapi.utils.rotate_1psidts")
    module.save_cookies = lambda *args, **kwargs: None
    module.clear_cookies_cache = lambda *args, **kwargs: None
    token._get_cookies_cache_path = lambda *args, **kwargs: None
    token.load_browser_cookies = lambda *args, **kwargs: {}
    rotate._get_cookies_cache_path = lambda *args, **kwargs: None
    if not getattr(module, "_invisibleai_upload_adapter", False):
        upload = module.upload_file
        parse_name = module.parse_file_name

        async def upload_png(file, *args, **kwargs):
            if isinstance(file, ImageBuffer):
                kwargs.setdefault("filename", "selection.png")
            return await upload(file, *args, **kwargs)

        module.upload_file = upload_png
        module.parse_file_name = lambda file: "selection.png" if isinstance(file, ImageBuffer) else parse_name(file)
        module._invisibleai_upload_adapter = True
    return module.GeminiClient


async def execute(request, client_factory=None):
    from gemini_webapi.constants import AccountStatus
    from gemini_webapi.exceptions import AuthError, ModelInvalidError

    cookies = request.get("cookies", {})
    if not isinstance(cookies, dict) or not cookies.get("__Secure-1PSID"):
        raise AuthError()
    if any(k not in ("__Secure-1PSID", "__Secure-1PSIDTS") for k in cookies):
        raise ValueError()
    if any(not isinstance(v, str) or len(v) > 900 or any(ord(c) <= 32 or ord(c) >= 127 for c in v) for v in cookies.values()):
        raise ValueError()
    client_factory = client_factory or harden_library()
    client = client_factory(cookies["__Secure-1PSID"], cookies.get("__Secure-1PSIDTS", ""), proxy=None)
    image = None
    try:
        await client.init(timeout=min(max(int(request.get("timeout", 90)), 10), 300), auto_close=False, auto_refresh=False, verbose=False)
        # Guest/blocked sessions must never count as a successful cookie connection or generate answers.
        if client.account_status != AccountStatus.AVAILABLE:
            raise AuthError()
        models = [m for m in (client.list_models() or []) if m.is_available]
        if request.get("operation") == "models":
            return {"models": [{"id": m.model_id, "name": m.display_name} for m in models]}
        if request.get("operation") != "generate":
            raise ValueError()
        model_id = request.get("model")
        if model_id not in {m.model_id for m in models}:
            raise ModelInvalidError()
        model = client.resolve_model(model_id)
        # Translate an OpenAI-style messages array to the maintained Gemini client's text interface.
        messages = request.get("messages", [])
        if not isinstance(messages, list) or len(messages) != 2:
            raise ValueError()
        prompt = "\n\n".join(str(m.get("content", "")) for m in messages)
        if len(prompt) > 55000:
            raise ValueError()
        if request.get("imageBase64"):
            data = base64.b64decode(request["imageBase64"], validate=True)
            if len(data) > 5 * 1024 * 1024 or not data.startswith(b"\x89PNG\r\n\x1a\n"):
                raise ValueError()
            image = ImageBuffer(data)
        answer = await client.generate_content(prompt, files=[image] if image else None, model=model, temporary=True)
        return {"content": answer.text, "provider": "gemini", "model": model_id, "usage": None, "finishReason": "stop"}
    finally:
        if image is not None:
            image.close()
        await client.close()


def error_code(error):
    # Classify by exception type only: never serialize exception messages, headers, or traces.
    name = type(error).__name__
    status = getattr(getattr(error, "response", None), "status_code", None)
    if status in (401, 403):
        return "auth"
    if status == 429:
        return "rate_limit"
    if name == "AuthError":
        return "auth"
    if name in ("UsageLimitExceededError", "TemporarilyBlockedError"):
        return "rate_limit"
    if name in ("TimeoutError", "Timeout", "CancelledError"):
        return "timeout"
    if name == "ModelInvalidError":
        return "model"
    if isinstance(error, (ModuleNotFoundError, ImportError)):
        return "dependency"
    if isinstance(error, (ValueError, TypeError, KeyError)):
        return "format"
    return "unavailable"


def main():
    result = None
    try:
        raw = sys.stdin.buffer.readline(MAX_INPUT + 1)
        if len(raw) > MAX_INPUT:
            raise ValueError()
        request = json.loads(raw)
        if not isinstance(request, dict):
            raise TypeError()
        # Never let dependency print()/warnings/debug sinks contaminate protocol output or diagnostics.
        with open(os.devnull, "w", encoding="utf-8") as sink, contextlib.redirect_stdout(sink), contextlib.redirect_stderr(sink):
            harden_library()
            result = asyncio.run(execute(request))
    except BaseException as error:  # noqa: BLE001 - protocol boundary must never expose dependency traces
        result = {"errorCode": error_code(error)}
    sys.stdout.write(json.dumps(result, ensure_ascii=True, separators=(",", ":")))
    sys.stdout.flush()


if __name__ == "__main__":
    main()
