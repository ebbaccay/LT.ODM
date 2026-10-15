import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Settings > Import pictures: style pictures from files named after the style (/api/v1/style-library/image-imports, Admin only).

/** Exact / Suffix (_H, -H, _L, _LH) are the style itself and are ticked; Variant (letter version) and Root are offered. */
export type ImageMatchKind = 'Exact' | 'Suffix' | 'Variant' | 'Root';
export type CropOutcome = 'Cropped' | 'Whole' | 'NotFound';
export type PictureField = 'Image' | 'Sketch';

/** Garment box in pixels of the upright picture. */
export interface CropBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CropSuggestion {
  width: number;
  height: number;
  box: CropBox | null;
  outcome: CropOutcome;
  /** Model: the image model chose the garment; Rules: shape only (model not installed or AI off). */
  method: 'Model' | 'Rules';
  /** false = check the box. */
  sure: boolean;
}

/** Sketch: goes to the style's sketch; Photo: the made garment, goes to the style's picture. */
export interface PictureKind {
  /** null = cannot tell. */
  kind: 'Sketch' | 'Photo' | null;
  /** false = close call: check it. */
  sure: boolean;
  method: 'Model' | 'Rules';
}

export interface StyleImageMatch {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string | null;
  modelName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  isActive: boolean;
  match: ImageMatchKind;
  ticked: boolean;
}

export interface StyleImageAnalysis {
  fileName: string;
  /** Style number read from the file name (before its first "_"), or null. */
  fileKey: string | null;
  crop: CropSuggestion;
  kind: PictureKind;
  matches: StyleImageMatch[];
}

/**
 * "Check existing pictures": a style whose stored picture looks like a sketch (or sketch like a photo).
 * Conflict = in the wrong place, but the other place already holds the right kind: left alone.
 */
export interface ExistingPicture {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string | null;
  modelName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  imageKind: PictureKind | null;
  sketchKind: PictureKind | null;
  action: 'MoveToSketch' | 'MoveToPicture' | 'Swap' | 'Conflict';
  sure: boolean;
}

export interface ExistingPicturesCheck {
  stylesChecked: number;
  filesChecked: number;
  filesUnreadable: number;
  suggestions: ExistingPicture[];
}

export interface StyleImageSaveResult {
  imageUrl: string;
  width: number;
  height: number;
  enhanced: boolean;
  updated: number[];
  skipped: number[];
}

export interface StyleImageImportStatus {
  aiEnabled: boolean;
  cropModel: boolean;
  cropModelProblem: string | null;
  upscaler: boolean;
  upscalerProblem: string | null;
}

export interface SaveOptions {
  box: CropBox | null;
  enhance: boolean;
  field: PictureField;
  overwrite: boolean;
  styleIds: number[];
}

@Injectable({ providedIn: 'root' })
export class StyleImageImportService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/style-library/image-imports`;

  status(): Observable<StyleImageImportStatus> {
    return this.http.get<StyleImageImportStatus>(`${this.api}/status`);
  }

  analyse(file: File): Observable<StyleImageAnalysis> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<StyleImageAnalysis>(`${this.api}/analyse`, form);
  }

  /** The picture as it would be saved (PNG). */
  preview(file: File, box: CropBox | null, enhance: boolean): Observable<Blob> {
    return this.http.post(`${this.api}/preview`, this.form(file, box, enhance), { responseType: 'blob' });
  }

  save(file: File, o: SaveOptions): Observable<StyleImageSaveResult> {
    const form = this.form(file, o.box, o.enhance);
    form.append('field', o.field);
    form.append('overwrite', String(o.overwrite));
    form.append('styleIds', o.styleIds.join(','));
    return this.http.post<StyleImageSaveResult>(`${this.api}/save`, form);
  }

  /** Checks every stored style picture and sketch; nothing changes. */
  checkExisting(): Observable<ExistingPicturesCheck> {
    return this.http.post<ExistingPicturesCheck>(`${this.api}/existing/check`, null);
  }

  /** Swaps picture and sketch of the styles given (only where they still hold what the check saw). */
  moveExisting(items: ExistingPicture[]): Observable<{ moved: number[]; skipped: number[] }> {
    // PascalCase: the API passes these to SQL as JSON.
    const body = { items: items.map((i) => ({ StyleId: i.styleId, ImageUrl: i.imageUrl, SketchUrl: i.sketchUrl })) };
    return this.http.post<{ moved: number[]; skipped: number[] }>(`${this.api}/existing/move`, body);
  }

  private form(file: File, box: CropBox | null, enhance: boolean): FormData {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('enhance', String(enhance));
    if (box) {
      form.append('x', String(Math.round(box.x)));
      form.append('y', String(Math.round(box.y)));
      form.append('width', String(Math.round(box.width)));
      form.append('height', String(Math.round(box.height)));
    }
    return form;
  }
}

export function imageImportErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string } | null;
    if (err.status === 0) return 'The server could not be reached. Check your connection and try again.';
    if (err.status === 403) return 'You need the Admin role to import pictures.';
    if (err.status === 413) return 'The picture is too large. Files must be 10 MB or smaller.';
    if (body?.title && err.status < 500) return body.title;
  }
  return 'The picture could not be processed. Please try again.';
}
