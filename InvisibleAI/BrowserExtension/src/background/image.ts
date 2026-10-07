export interface Region { x: number; y: number; width: number; height: number; viewportWidth: number; viewportHeight: number }
export function cropBounds(region: Region, width: number, height: number): { x: number; y: number; width: number; height: number } {
  if (![region.x, region.y, region.width, region.height, region.viewportWidth, region.viewportHeight, width, height].every(Number.isFinite) || width <= 0 || height <= 0 || region.viewportWidth <= 0 || region.viewportHeight <= 0 ||
    region.x < 0 || region.y < 0 || region.width < 4 || region.height < 4 || region.x + region.width > region.viewportWidth + 1 || region.y + region.height > region.viewportHeight + 1 || width * height > 40_000_000) throw new Error("Could not read the selected region.");
  const sx = width / region.viewportWidth, sy = height / region.viewportHeight;
  const x = Math.floor(region.x * sx), y = Math.floor(region.y * sy);
  return { x, y, width: Math.min(width - x, Math.ceil((region.x + region.width) * sx) - x), height: Math.min(height - y, Math.ceil((region.y + region.height) * sy) - y) };
}
export async function cropImage(dataUrl: string, region: Region): Promise<string> {
  let bitmap: ImageBitmap | undefined, canvas: OffscreenCanvas | undefined, bytes: Uint8Array | undefined;
  try {
    const blob = await (await fetch(dataUrl)).blob();
    // Read PNG dimensions before decoding to cap allocation, including high-DPI viewports.
    const header = new DataView(await blob.slice(0, 24).arrayBuffer());
    if (blob.size > 32 * 1024 * 1024 || header.byteLength < 24 || header.getUint32(0) !== 0x89504e47 || header.getUint32(4) !== 0x0d0a1a0a) throw new Error();
    const bounds = cropBounds(region, header.getUint32(16), header.getUint32(20));
    bitmap = await createImageBitmap(blob);
    const scale = Math.min(1, 2048 / Math.max(bounds.width, bounds.height));
    canvas = new OffscreenCanvas(Math.max(1, Math.round(bounds.width * scale)), Math.max(1, Math.round(bounds.height * scale)));
    const ctx = canvas.getContext("2d"); if (!ctx) throw new Error();
    ctx.drawImage(bitmap, bounds.x, bounds.y, bounds.width, bounds.height, 0, 0, canvas.width, canvas.height);
    bitmap.close(); bitmap = undefined;
    const cropped = await canvas.convertToBlob({ type: "image/png" });
    if (cropped.size > 5 * 1024 * 1024) throw new Error("Selected region is too large. Select a smaller region.");
    bytes = new Uint8Array(await cropped.arrayBuffer());
    let binary = "";
    for (let i = 0; i < bytes.length; i += 32768) binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
    return btoa(binary);
  // Discard decoder/fetch traces; the UI receives only the bounded public error.
  // eslint-disable-next-line preserve-caught-error
  } catch (error) { throw new Error(error instanceof Error && error.message.startsWith("Selected region") ? error.message : "Could not read the selected region."); }
  finally { bytes?.fill(0); bitmap?.close(); if (canvas) { canvas.width = 1; canvas.height = 1; } }
}
