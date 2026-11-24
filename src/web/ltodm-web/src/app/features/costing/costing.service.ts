import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { hasAnyRole } from '../../core/auth/library-roles';

// Costing (/api/v1/costing, roadmap 3.16): prices per IO from the ERP. Same roles as CostingRoles in the API, which enforces them.
// Designers see FOB too (2026-11-11), to design to a style's price; quotes and prices sent to clients stay with QUOTE_EDITORS.
const COSTING_READERS = ['Admin', 'Merchandiser', 'Designer', 'Costing'] as const;
const QUOTE_EDITORS = ['Admin', 'Merchandiser', 'Costing'] as const;

/** A style's prices over its IOs, in one currency. The cost groups and avgNet are null for those who may only see FOB. */
export interface StylePriceSummary {
  styleId: number;
  styleNo: string;
  seasonCode: string;
  customerCode: string;
  modelName: string | null;
  productTypeName: string | null;
  imageUrl: string | null;
  sketchUrl: string | null;
  currencyCode: string;
  ios: number;
  totalQty: number;
  latestFob: number;
  latestCostDate: string | null;
  minFob: number;
  maxFob: number;
  avgFob: number;
  avgFabric: number | null;
  avgTrims: number | null;
  avgMaking: number | null;
  avgOther: number | null;
  avgNet: number | null;
}

export interface IoPrice {
  ioNo: string;
  plantCode: string | null;
  plantName: string | null;
  orderQty: number | null;
  lcoDate: string | null;
  planDeliveryDate: string | null;
  tcSmv: number | null;
  costingVersion: number;
  /** PCOSTING = pre-costing, FCOSTING = final costing. */
  costType: string;
  currencyCode: string;
  costDate: string | null;
  fob: number;
  fabric: number | null;
  trims: number | null;
  making: number | null;
  other: number | null;
  netBalance: number | null;
  percToFob: number | null;
  /** A final costing exists but its main fabric is not computed yet. */
  finalPending: boolean;
  /** Below the small-order quantity: listed, but left out of the figures. */
  isSmall: boolean;
}

export interface CostingImport {
  importId: number;
  fileName: string;
  rowsRead: number;
  added: number;
  updated: number;
  unmatched: number;
  importedBy: string;
  importedUtc: string;
}

export interface CostingPage {
  items: StylePriceSummary[];
  total: number;
  lastImport: CostingImport | null;
  showCost: boolean;
}

export interface StylePrices {
  summaries: StylePriceSummary[];
  ios: IoPrice[];
  unfinished: number;
  showCost: boolean;
  smallOrderQty: number;
}

/** What a style's IO history says it costs. */
export interface StylePriceHint {
  styleId: number;
  source: 'Own' | 'Family' | 'Origin';
  sourceStyleNo: string;
  currencyCode: string;
  latestFob: number;
  minFob: number;
  maxFob: number;
  avgFob: number;
  ios: number;
  totalQty: number;
  latestCostDate: string | null;
  latestIoNo: string | null;
}

export type QuoteBasisKind = 'Own' | 'Family' | 'Origin' | 'Estimate' | 'Typed';

/** An estimated FOB for a garment with no IO prices, from similar priced styles. The cost parts are null for those who may only see FOB. */
export interface Estimate {
  category: string;
  peers: number;
  peerStyles: string;
  currencyCode: string;
  estimatedFob: number;
  smv: number | null;
  plantCode: string | null;
  cpm: number | null;
  cpmFromRate: boolean;
  making: number | null;
  fabric: number | null;
  trims: number | null;
  other: number | null;
  netShare: number | null;
}

export interface Quote {
  version: number;
  fob: number;
  currencyCode: string;
  basisKind: QuoteBasisKind;
  basisText: string | null;
  quotedBy: string;
  quotedUtc: string;
}

export interface PackageItemQuotes {
  packageItemId: number;
  styleId: number;
  suggestion: StylePriceHint | null;
  /** Newest first. */
  quotes: Quote[];
  /** When there is no suggestion: estimated from similar styles of the garment. */
  estimate: Estimate | null;
}

export interface ClientBand {
  priceMin: number | null;
  priceMax: number | null;
  currency: string;
}

export interface PackageQuotes {
  band: ClientBand | null;
  items: PackageItemQuotes[];
}

/** A plant's cost per minute from a date, for orders from minOrderQty. inForce: the rate used today. */
export interface PlantRate {
  rateId: number;
  plantCode: string;
  plantName: string;
  effectiveDate: string;
  minOrderQty: number;
  salesTerm: string;
  currencyCode: string;
  cost: number;
  isDeleted: boolean;
  inForce: boolean;
  createdBy: string;
  createdUtc: string;
  updatedBy: string | null;
  updatedUtc: string | null;
}

export interface PlantRates {
  rates: PlantRate[];
  plants: { code: string; name: string }[];
}

export interface SavePlantRate {
  plantCode: string;
  effectiveDate: string;
  minOrderQty: number;
  salesTerm: string;
  currency: string;
  cost: number;
}

/** A garment's prices: the low, typical (median) and high latest FOB of its styles with IO prices, and their TC SMV. */
export interface PriceGuideRow {
  category: string;
  categoryName: string;
  currencyCode: string;
  styles: number;
  lowFob: number;
  typicalFob: number;
  highFob: number;
  lowSmv: number | null;
  typicalSmv: number | null;
  highSmv: number | null;
}

export interface ConceptCost {
  conceptId: number;
  name: string;
  categoryName: string | null;
  customerCode: string | null;
  targetPrice: string | null;
  estimatedFob: number | null;
  currencyCode: string | null;
  peers: number;
  status: 'Within' | 'Over' | 'Under' | 'NoCompare' | 'NoTarget' | 'NoCategory' | 'NoEstimate';
  gap: number | null;
}

export interface CostingOverview {
  health: {
    lastImportUtc: string | null;
    lastImportFile: string | null;
    lastImportBy: string | null;
    lastImportUnmatched: number;
    stylesWithPrices: number;
    activeStyles: number;
    ios: number;
    iosUnfinished: number;
    finalPending: number;
    smallIos: number;
    plantsWithoutRate: number;
    plantsWithoutRateList: string | null;
  };
  netByGarment: { category: string; currencyCode: string; styles: number; ios: number; qty: number; value: number; netPct: number | null }[];
  netDrops: { styleId: number; styleNo: string; ioNo: string; costDate: string | null; fob: number; currencyCode: string; ioPct: number; stylePct: number }[];
  quotes: { noQuote: number; outOfBand: number; estimated: number; packages: number };
  quoteItems: { packageId: number; packageName: string; styleNo: string; kind: 'NoQuote' | 'OutOfBand' | 'Estimate'; fob: number | null; currencyCode: string | null }[];
  netGap: number;
}

export interface CostingImportResult {
  import: CostingImport;
  unmatchedStyles: { customer: string; season: string; styleNo: string; rows: number }[];
  issues: string[];
}

@Injectable({ providedIn: 'root' })
export class CostingService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly api = `${environment.apiBaseUrl}/api/v1/costing`;

  readonly canRead = computed(() => hasAnyRole(this.auth.user()?.roles, COSTING_READERS));
  /** Set quotes and put prices on line sheets and share links. */
  readonly canQuote = computed(() => hasAnyRole(this.auth.user()?.roles, QUOTE_EDITORS));
  readonly canImport = computed(() => hasAnyRole(this.auth.user()?.roles, ['Admin']));
  /** Plant cost per minute is factory cost: Admin and Costing only. */
  readonly canKeepRates = computed(() => hasAnyRole(this.auth.user()?.roles, ['Admin', 'Costing']));

  list(search: string, customer: string, season: string, page: number, pageSize: number): Observable<CostingPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) params = params.set('search', search.trim());
    if (customer) params = params.set('customer', customer);
    if (season) params = params.set('season', season);
    return this.http.get<CostingPage>(`${this.api}/styles`, { params });
  }

  style(styleId: number): Observable<StylePrices> {
    return this.http.get<StylePrices>(`${this.api}/styles/${styleId}`);
  }

  /** What each style's IO history says it costs (at most 100 styles). */
  hints(styleIds: number[]): Observable<StylePriceHint[]> {
    return this.http.post<StylePriceHint[]>(`${this.api}/prices`, { styleIds: styleIds.slice(0, 100) });
  }

  packageQuotes(packageId: number): Observable<PackageQuotes> {
    return this.http.get<PackageQuotes>(`${environment.apiBaseUrl}/api/v1/style-packages/${packageId}/quotes`);
  }

  saveQuote(packageId: number, packageItemId: number, quote: { fob: number; currency: string; basisKind: QuoteBasisKind; basisText: string }): Observable<void> {
    return this.http.put<void>(`${environment.apiBaseUrl}/api/v1/style-packages/${packageId}/items/${packageItemId}/quote`, quote);
  }

  /** null when no priced style is of that garment (204). */
  estimate(category: string, customerCode: string | null): Observable<Estimate | null> {
    const params: Record<string, string> = { category };
    if (customerCode) params['customer'] = customerCode;
    return this.http.get<Estimate | null>(`${this.api}/estimate`, { params });
  }

  guide(): Observable<PriceGuideRow[]> {
    return this.http.get<PriceGuideRow[]>(`${this.api}/guide`);
  }

  myConcepts(): Observable<ConceptCost[]> {
    return this.http.get<ConceptCost[]>(`${this.api}/my-concepts`);
  }

  /** Admin and Costing. */
  overview(): Observable<CostingOverview> {
    return this.http.get<CostingOverview>(`${this.api}/overview`);
  }

  rates(withDeleted: boolean): Observable<PlantRates> {
    return this.http.get<PlantRates>(`${this.api}/rates`, { params: { withDeleted } });
  }

  saveRate(rateId: number | null, rate: SavePlantRate): Observable<{ rateId: number }> {
    return rateId === null ? this.http.post<{ rateId: number }>(`${this.api}/rates`, rate) : this.http.put<{ rateId: number }>(`${this.api}/rates/${rateId}`, rate);
  }

  deleteRate(rateId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/rates/${rateId}`);
  }

  importFile(file: File): Observable<CostingImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<CostingImportResult>(`${this.api}/imports`, form);
  }
}
