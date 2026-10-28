import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, firstValueFrom, map } from 'rxjs';
import { Tone } from '@tms/shared/ui/tones';
import { safeFileName, saveBlob } from '../style-library/style-library.service';

// Style Packages (/api/v1/style-packages): library styles offered to a client.
// Reading: Style Library readers. Changing: Admin, Merchandiser (the API checks both). No prices yet (roadmap 5.19).

export const PACKAGE_STATUSES = ['Draft', 'Sent', 'Viewed', 'Sampling', 'Ordered', 'Closed'] as const;
export type PackageStatus = (typeof PACKAGE_STATUSES)[number];

export const STATUS_TONE: Record<PackageStatus, Tone> = {
  Draft: 'neutral', Sent: 'primary', Viewed: 'violet', Sampling: 'amber', Ordered: 'green', Closed: 'neutral',
};

/** Where styles were added from (kept for the hit rate later). */
export type PackageSource = 'Styles' | 'StylePage' | 'Similar' | 'ImageSearch' | 'Assistant' | 'Concept' | 'Copy' | 'Offer';

export interface PackageListItem {
  packageId: number;
  name: string;
  customerCode: string;
  customerName: string;
  seasonCode: string | null;
  status: PackageStatus;
  ownerName: string;
  sentUtc: string | null;
  items: number;
  /** The first item's picture or sketch. */
  coverUrl: string | null;
  lastChangedUtc: string;
}

export interface PackagePage {
  items: PackageListItem[];
  total: number;
}

export interface PackageFilter {
  search?: string;
  customer?: string;
  season?: string;
  status?: string;
  mine?: boolean;
  skip?: number;
  take?: number;
}

/** RowVer: base64; send back unchanged when saving or deleting. */
export interface PackageHeader {
  packageId: number;
  name: string;
  customerCode: string;
  customerName: string;
  brandCode: string | null;
  seasonCode: string | null;
  description: string | null;
  status: PackageStatus;
  ownerName: string;
  sentUtc: string | null;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
}

/** offered: in the offer; missing: a chosen code no longer on the style. */
export interface PackageColorway {
  colorwayCode: string;
  colorwayName: string | null;
  status: string | null;
  imageUrl: string | null;
  offered: boolean;
  missing: boolean;
}

/** A key material of the style: up to 3 fabrics (largest consumption first) and 4 trims. */
export interface PackageMaterial {
  kind: 'Fabric' | 'Trim';
  materialCode: string;
  description: string | null;
  /** Fibre composition read by the description reader (null until read). */
  composition: string | null;
  recycled: boolean;
}

export interface PackageItem {
  packageItemId: number;
  styleId: number;
  sortOrder: number;
  pitchNote: string | null;
  internalNote: string | null;
  allColorways: boolean;
  addedFrom: PackageSource | null;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  description: string | null;
  modelName: string | null;
  productTypeCode: string | null;
  productTypeName: string | null;
  gender: string | null;
  weaveTypeCode: string | null;
  weaveTypeName: string | null;
  garmentLeadTimeDays: number | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  isActive: boolean;
  /** Not of the package's brand any more (the customer was changed after it was added). */
  otherBrand: boolean;
  /** The style's fabric BOM lines and how many are written as recycled. */
  fabricLines: number;
  recycledFabricLines: number;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string;
  colorways: PackageColorway[];
  materials: PackageMaterial[];
  /** The clients' answers through share links (newest first). */
  feedback: PackageFeedback[] | null;
  /** What became of the style (hit rate): Sampling, Ordered, Dropped, or null (not known yet). */
  outcome: PackageOutcome | null;
  outcomeBy: string | null;
  outcomeUtc: string | null;
}

export const PACKAGE_OUTCOMES = ['Sampling', 'Ordered', 'Dropped'] as const;
export type PackageOutcome = (typeof PACKAGE_OUTCOMES)[number];

export const OUTCOME_TONE: Record<PackageOutcome, Tone> = { Sampling: 'amber', Ordered: 'green', Dropped: 'neutral' };

/**
 * Styles offered and what came of them. Votes: one per person per link per style. Per style: allLiked (only likes),
 * mixed (both), allPassed (only passes); answered = styles with any vote. key = garment, client or source.
 */
export interface HitRateRow {
  key: string | null;
  offered: number;
  answered: number;
  likeVotes: number;
  passVotes: number;
  allLiked: number;
  mixed: number;
  allPassed: number;
  sampling: number;
  ordered: number;
  dropped: number;
  packages: number;
}

export interface HitRate {
  totals: HitRateRow;
  byGarment: HitRateRow[];
  byClient: HitRateRow[];
  bySource: HitRateRow[];
}

/** A client's answer on a style through a share link. */
export interface PackageFeedback {
  shareId: number;
  label: string;
  verdict: 'Like' | 'Pass' | null;
  comment: string | null;
  name: string | null;
  updatedUtc: string;
}

/** A share link of a package (the client opens /share/{token} without signing in). */
export interface PackageShare {
  shareId: number;
  label: string;
  expiresUtc: string;
  revokedUtc: string | null;
  revokedBy: string | null;
  views: number;
  firstViewedUtc: string | null;
  lastViewedUtc: string | null;
  createdBy: string;
  createdUtc: string;
  active: boolean;
  likes: number;
  passes: number;
  comments: number;
  /** Active and kept encrypted: Copy link can show it again (links made before 2026-10-28 cannot). */
  canCopy: boolean;
}

/** A new link's secret: shown once (only its fingerprint is kept). */
export interface CreatedShare {
  shareId: number;
  token: string;
  expiresUtc: string;
}

export interface PackageDetail {
  package: PackageHeader;
  items: PackageItem[];
}

/** A package a style is in (style page). */
export interface StylePackage {
  packageId: number;
  name: string;
  customerCode: string;
  seasonCode: string | null;
  status: PackageStatus;
  ownerName: string;
  packageItemId: number;
}

export interface SavePackage {
  rowVer?: string | null;
  name: string;
  customerCode: string;
  seasonCode?: string | null;
  description?: string | null;
  status?: PackageStatus;
  ownerName?: string | null;
}

export type AddResult = 'Added' | 'AlreadyIn' | 'OtherBrand' | 'NotFound';

export interface PackageAddResult {
  styleId: number;
  styleNo: string | null;
  seasonCode: string | null;
  customerCode: string | null;
  result: AddResult;
  packageItemId: number | null;
}

/** The new style made by copying another customer's style into the package. */
export interface PackageCopyResult {
  styleId: number;
  packageItemId: number;
}

export interface SavePackageItem {
  rowVer: string;
  pitchNote: string | null;
  internalNote: string | null;
  allColorways: boolean;
  colorwayCodes: string[];
}

/** What to offer (roadmap 5.14): the client in the library. */
export interface OfferProfile {
  brand: string;
  customerName: string;
  accounts: number;
  styles: number;
  firstSeason: string | null;
  latestSeason: string | null;
}

export interface OfferCategory {
  category: string;
  styles: number;
  latestSeason: string | null;
  /** Styles of this garment offered to the client in packages sent, and won (sampling or ordered). */
  offered: number;
  won: number;
}

export type OfferSection = 'Repitch' | 'Gap' | 'Fabric';

/**
 * Repitch: detail1 colourways, detail2 seasons since. Gap: detail1 the brand's styles of the garment, detail2 LT's.
 * Fabric: detail1 shared fabrics. sameBrand false = offered as a copy.
 */
export interface OfferSuggestion {
  section: OfferSection;
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  description: string | null;
  productTypeName: string | null;
  category: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  sameBrand: boolean;
  detail1: number | null;
  detail2: number | null;
  /** The client's record on this garment (offered in packages sent / won); results move suggestions up or down. */
  categoryOffered: number;
  categoryWon: number;
}

/** profile null = the client has no styles in the library yet. */
export interface Offer {
  profile: OfferProfile | null;
  categories: OfferCategory[];
  suggestions: OfferSuggestion[];
}

@Injectable({ providedIn: 'root' })
export class PackagesService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/style-packages`;

  list(f: PackageFilter): Observable<PackagePage> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(f)) if (v !== undefined && v !== null && v !== '' && v !== false) params = params.set(k, String(v));
    return this.http.get<PackagePage>(this.api, { params });
  }

  /** What to offer the customer's brand (rules, no AI); styles already in packageId are left out. */
  offer(customer: string, packageId: number | null, top = 12): Observable<Offer> {
    let params = new HttpParams().set('customer', customer).set('top', top);
    if (packageId) params = params.set('packageId', packageId);
    return this.http.get<Offer>(`${this.api}/offer`, { params });
  }

  get(packageId: number): Observable<PackageDetail> {
    return this.http.get<PackageDetail>(`${this.api}/${packageId}`);
  }

  forStyle(styleId: number): Observable<StylePackage[]> {
    return this.http.get<StylePackage[]>(`${this.api}/for-style/${styleId}`);
  }

  create(p: SavePackage): Observable<number> {
    return this.http.post<{ packageId: number }>(this.api, p).pipe(map((r) => r.packageId));
  }

  update(packageId: number, p: SavePackage): Observable<number> {
    return this.http.put<{ packageId: number }>(`${this.api}/${packageId}`, p).pipe(map((r) => r.packageId));
  }

  delete(packageId: number, rowVer: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/${packageId}`, { params: { rowVer } });
  }

  addStyles(packageId: number, styleIds: number[], addedFrom: PackageSource): Observable<PackageAddResult[]> {
    return this.http.post<PackageAddResult[]>(`${this.api}/${packageId}/items`, { styleIds, addedFrom });
  }

  /** Offers another customer's style as a new style of the package's customer (know-how only) and adds it. */
  addCopy(packageId: number, styleId: number, seasonCode: string, styleNo: string): Observable<PackageCopyResult> {
    return this.http.post<PackageCopyResult>(`${this.api}/${packageId}/items/copy`, { styleId, seasonCode, styleNo });
  }

  saveItem(packageId: number, packageItemId: number, item: SavePackageItem): Observable<void> {
    return this.http.put<void>(`${this.api}/${packageId}/items/${packageItemId}`, item);
  }

  removeItems(packageId: number, packageItemIds: number[]): Observable<void> {
    return this.http.post<void>(`${this.api}/${packageId}/items/remove`, { packageItemIds });
  }

  reorder(packageId: number, packageItemIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.api}/${packageId}/items/order`, { packageItemIds });
  }

  /** What became of a style offered (null = not known yet). */
  setOutcome(packageId: number, packageItemId: number, outcome: PackageOutcome | null): Observable<void> {
    return this.http.put<void>(`${this.api}/${packageId}/items/${packageItemId}/outcome`, { outcome });
  }

  /** Hit rate of packages sent from / to (yyyy-MM-dd, both days included), optionally for one customer's brand. */
  hitRate(customer: string | null, from: string | null, to: string | null): Observable<HitRate> {
    let params = new HttpParams();
    if (customer) params = params.set('customer', customer);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<HitRate>(`${this.api}/hit-rate`, { params });
  }

  shares(packageId: number): Observable<PackageShare[]> {
    return this.http.get<PackageShare[]>(`${this.api}/${packageId}/shares`);
  }

  createShare(packageId: number, label: string, expiresInDays: number): Observable<CreatedShare> {
    return this.http.post<CreatedShare>(`${this.api}/${packageId}/shares`, { label, expiresInDays });
  }

  /** An active link's secret again, to re-send it (editors). */
  shareLink(packageId: number, shareId: number): Observable<string> {
    return this.http.get<{ token: string }>(`${this.api}/${packageId}/shares/${shareId}/link`).pipe(map((r) => r.token));
  }

  revokeShare(packageId: number, shareId: number): Observable<void> {
    return this.http.post<void>(`${this.api}/${packageId}/shares/${shareId}/revoke`, {});
  }

  /** Downloads the line sheet for the client: pdf (cards with pictures) or xlsx (one row per style). */
  async downloadLineSheet(packageId: number, name: string, format: 'pdf' | 'xlsx'): Promise<void> {
    const blob = await firstValueFrom(this.http.get(`${this.api}/${packageId}/line-sheet.${format}`, { responseType: 'blob' }));
    saveBlob(blob, `${safeFileName(name)}_line_sheet.${format}`);
  }
}
