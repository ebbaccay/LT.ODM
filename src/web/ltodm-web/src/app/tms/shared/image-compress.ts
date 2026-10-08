/**
 * Resizes an image to at most maxDimension px per side before uploading (the server makes the final, smaller copy).
 * Photos are re-encoded as JPEG; PNG and GIF images (usually sketches and graphics) stay PNG so line work stays sharp and
 * transparent backgrounds do not turn black. The server then keeps whichever of JPEG or PNG is smaller.
 */
export function compressImage(file: File, maxDimension: number, quality = 0.9): Promise<Blob> {
  return new Promise((resolve, reject) => {
    if (!file.type.startsWith('image/')) {
      reject(new Error('Not an image'));
      return;
    }
    const keepPng = file.type === 'image/png' || file.type === 'image/gif';
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Image load error'));
    };
    img.onload = () => {
      URL.revokeObjectURL(url);
      const ratio = Math.min(1, maxDimension / img.width, maxDimension / img.height);
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(img.width * ratio);
      canvas.height = Math.round(img.height * ratio);
      const ctx = canvas.getContext('2d')!;
      if (!keepPng) {
        // JPEG has no transparency: paint white first (otherwise clear areas come out black).
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
      }
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error('canvas.toBlob failed'))),
        keepPng ? 'image/png' : 'image/jpeg',
        quality,
      );
    };
    img.src = url;
  });
}
