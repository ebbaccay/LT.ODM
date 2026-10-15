import { ChangeDetectionStrategy, Component, ElementRef, computed, input, model, viewChild } from '@angular/core';
import { CropBox } from '@services/style-image-import.service';

type Drag =
  | { kind: 'move'; startX: number; startY: number; from: CropBox }
  | { kind: 'resize'; corner: Corner; from: CropBox }
  | { kind: 'draw'; startX: number; startY: number };
type Corner = 'nw' | 'ne' | 'sw' | 'se';

/** Smallest box (pixels of the picture) the editor allows. */
const MIN = 8;

/**
 * Shows a picture with the garment box on it. Drag inside the box to move it, drag a corner to resize, drag anywhere
 * else to draw a new box. With the box focused, arrow keys move it (Shift + arrows: resize; 10 px with Alt).
 * Coordinates are pixels of the picture (naturalWidth x naturalHeight), whatever size it is shown at. Null = whole picture.
 */
@Component({
  selector: 'app-crop-box-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      #frame
      class="relative touch-none overflow-hidden rounded-md border bg-white select-none"
      [style.aspect-ratio]="naturalWidth() + ' / ' + naturalHeight()"
      (pointerdown)="start($event, null)"
      (pointermove)="move($event)"
      (pointerup)="end($event)"
      (pointercancel)="end($event)"
    >
      <img [src]="src()" alt="" class="pointer-events-none absolute inset-0 size-full object-contain" draggable="false" />
      @if (pct(); as p) {
        <div
          class="ring-primary absolute cursor-move shadow-[0_0_0_9999px_rgba(0,0,0,0.45)] ring-2 outline-none focus-visible:ring-4"
          [style.left.%]="p.left"
          [style.top.%]="p.top"
          [style.width.%]="p.width"
          [style.height.%]="p.height"
          tabindex="0"
          role="group"
          [attr.aria-label]="label()"
          (pointerdown)="start($event, 'move')"
          (keydown)="key($event)"
        >
          @for (c of corners; track c) {
            <span
              class="bg-primary border-background absolute size-3.5 rounded-full border-2"
              [class]="handleClass[c]"
              (pointerdown)="start($event, c)"
            ></span>
          }
        </div>
      }
    </div>
  `,
})
export class CropBoxEditor {
  readonly src = input.required<string>();
  readonly naturalWidth = input.required<number>();
  readonly naturalHeight = input.required<number>();
  readonly box = model<CropBox | null>(null);
  /** Accessible name of the box (with its size). */
  readonly label = input('Garment box');

  private readonly frame = viewChild.required<ElementRef<HTMLDivElement>>('frame');
  private drag: Drag | null = null;

  readonly corners: Corner[] = ['nw', 'ne', 'sw', 'se'];
  readonly handleClass: Record<Corner, string> = {
    nw: '-left-2 -top-2 cursor-nwse-resize',
    ne: '-right-2 -top-2 cursor-nesw-resize',
    sw: '-left-2 -bottom-2 cursor-nesw-resize',
    se: '-right-2 -bottom-2 cursor-nwse-resize',
  };

  readonly pct = computed(() => {
    const b = this.box();
    const w = this.naturalWidth();
    const h = this.naturalHeight();
    if (!b || !w || !h) return null;
    return { left: (b.x / w) * 100, top: (b.y / h) * 100, width: (b.width / w) * 100, height: (b.height / h) * 100 };
  });

  start(e: PointerEvent, what: 'move' | Corner | null): void {
    if (e.button !== 0) return;
    e.stopPropagation();
    e.preventDefault();
    const [x, y] = this.point(e);
    const b = this.box();
    if (what === 'move' && b) this.drag = { kind: 'move', startX: x, startY: y, from: b };
    else if (what && what !== 'move' && b) this.drag = { kind: 'resize', corner: what, from: b };
    else this.drag = { kind: 'draw', startX: x, startY: y };
    this.frame().nativeElement.setPointerCapture(e.pointerId);
  }

  move(e: PointerEvent): void {
    const d = this.drag;
    if (!d) return;
    const [x, y] = this.point(e);
    const W = this.naturalWidth();
    const H = this.naturalHeight();
    if (d.kind === 'move') {
      const nx = clamp(d.from.x + x - d.startX, 0, W - d.from.width);
      const ny = clamp(d.from.y + y - d.startY, 0, H - d.from.height);
      this.box.set({ ...d.from, x: nx, y: ny });
    } else if (d.kind === 'draw') {
      if (Math.abs(x - d.startX) < MIN && Math.abs(y - d.startY) < MIN) return;
      this.box.set(rect(d.startX, d.startY, x, y));
    } else {
      const f = d.from;
      const left = d.corner.includes('w') ? x : f.x;
      const right = d.corner.includes('e') ? x : f.x + f.width;
      const top = d.corner.includes('n') ? y : f.y;
      const bottom = d.corner.includes('s') ? y : f.y + f.height;
      this.box.set(rect(left, top, right, bottom));
    }
  }

  end(e: PointerEvent): void {
    if (!this.drag) return;
    this.drag = null;
    const el = this.frame().nativeElement;
    if (el.hasPointerCapture(e.pointerId)) el.releasePointerCapture(e.pointerId);
  }

  key(e: KeyboardEvent): void {
    const b = this.box();
    if (!b) return;
    const step = e.altKey ? 10 : 1;
    const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
    const dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;
    if (!dx && !dy) return;
    e.preventDefault();
    const W = this.naturalWidth();
    const H = this.naturalHeight();
    if (e.shiftKey) {
      const width = clamp(b.width + dx, MIN, W - b.x);
      const height = clamp(b.height + dy, MIN, H - b.y);
      this.box.set({ ...b, width, height });
    } else {
      this.box.set({ ...b, x: clamp(b.x + dx, 0, W - b.width), y: clamp(b.y + dy, 0, H - b.height) });
    }
  }

  /** Pointer position in picture pixels, kept inside the picture. */
  private point(e: PointerEvent): [number, number] {
    const r = this.frame().nativeElement.getBoundingClientRect();
    const x = ((e.clientX - r.left) / r.width) * this.naturalWidth();
    const y = ((e.clientY - r.top) / r.height) * this.naturalHeight();
    return [clamp(x, 0, this.naturalWidth()), clamp(y, 0, this.naturalHeight())];
  }
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(Math.max(v, lo), Math.max(lo, hi));
}

function rect(x0: number, y0: number, x1: number, y1: number): CropBox {
  const x = Math.round(Math.min(x0, x1));
  const y = Math.round(Math.min(y0, y1));
  return { x, y, width: Math.max(MIN, Math.round(Math.abs(x1 - x0))), height: Math.max(MIN, Math.round(Math.abs(y1 - y0))) };
}
