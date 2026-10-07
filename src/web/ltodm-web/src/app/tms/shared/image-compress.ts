/** Resizes an image to at most maxDimension px per side and re-encodes it as JPEG (as TMS did before uploading). */
export function compressImage(file: File, maxDimension: number, quality = 0.85): Promise<Blob> {
  return new Promise((resolve, reject) => {
    if (!file.type.startsWith('image/')) {
      reject(new Error('Not an image'));
      return;
    }
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
      canvas.getContext('2d')!.drawImage(img, 0, 0, canvas.width, canvas.height);
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('canvas.toBlob failed'))), 'image/jpeg', quality);
    };
    img.src = url;
  });
}
