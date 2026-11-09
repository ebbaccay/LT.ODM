import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Paged } from '../style-library/style-library.service';

// AI Studio > Library assistant (/api/v1/ai/library-assistant). The conversation lives in this page only; the last few
// turns are sent with each question so follow-ups work. The server keeps questions the AI answered (with the look-up it
// chose, never the answer) so a thumbs up can make them saved questions answered at once.

/** A capability: name is also the key of its texts (ai.assistant.tool.{name}). */
export interface LibraryTool {
  name: string;
  description: string;
  example: string;
  parameters: string[];
}

export interface LibraryTurn {
  question: string;
  tool: string | null;
  arguments: Record<string, string>;
}

/** Columns are keys (ai.assistant.col.*); rowLinks: an app address per row, or null; rowImages: a picture per row (thumbnail), or none. */
export interface LibraryTable {
  columns: string[];
  rows: (string | null)[][];
  rowLinks: (string | null)[];
  total: number;
  rowImages?: (string | null)[] | null;
}

/** kind is a key (ai.assistant.link.*) with text as its parameter. */
export interface LibraryLink {
  kind: string;
  text: string | null;
  url: string;
}

export interface LibraryAnswer {
  answer: string;
  /** rules: the phrase list (no AI). saved: a saved question (no AI). guide: a how-to guide by its words (no AI). ai: the AI picked the tool. none: not answered. */
  source: 'rules' | 'saved' | 'look' | 'guide' | 'ai' | 'none';
  tool: string | null;
  understood: string | null;
  arguments: Record<string, string>;
  table: LibraryTable | null;
  links: LibraryLink[];
  unmatched: string[];
  /** The code's own sentence is shown (AI not used, or its wording failed the number check). */
  answerFromFacts: boolean;
  /** Set when the answer can get a thumbs up / down (an AI answer that was recorded, or a saved question). */
  savedQuestionId: number | null;
  /** Synonyms read differently ("tees → TOP"); null or empty when none. */
  readAs?: string[] | null;
}

/** Unanswered: nobody could answer it (set by the assistant, for Admins to review). */
export type SavedQuestionStatus = 'Recorded' | 'Rule' | 'Removed' | 'Unanswered';

export type SynonymKind = 'productType' | 'customer' | 'season' | 'businessUnit' | 'gender' | 'weaveType' | 'words';

/** word: as people type it; value: a code of the kind's list, or plain words for 'words'. */
export interface Synonym {
  synonymId: number;
  word: string;
  kind: SynonymKind;
  value: string;
  changedBy: string | null;
  changedUtc: string | null;
}

/**
 * askCount: answered by the AI; ruleHits: answered from the saved look-up. changedHow: Feedback, Admin or Seed.
 * isPattern: a seeded pattern, matched with the filter words taken out.
 */
export interface SavedQuestion {
  savedQuestionId: number;
  question: string;
  tool: string;
  arguments: Record<string, string>;
  status: SavedQuestionStatus;
  askCount: number;
  ruleHits: number;
  lastAskedUtc: string;
  createdUtc: string;
  changedBy: string | null;
  changedUtc: string | null;
  changedHow: string | null;
  isPattern: boolean;
}

/** One step of a how-to guide; url: an app address ({customer} and {season} are filled from the question), or null. */
export interface GuideStep {
  text: string;
  url: string | null;
}

/** A how-to guide (ai.Guide). questions: the wordings that open it at once. */
export interface Guide {
  code: string;
  title: string;
  who: string | null;
  intro: string | null;
  steps: GuideStep[];
  isActive: boolean;
  questions: string[];
  changedBy: string | null;
  changedUtc: string | null;
  changedHow: string | null;
}

export type GuideInput = Pick<Guide, 'title' | 'who' | 'intro' | 'steps' | 'isActive' | 'questions'>;

@Injectable({ providedIn: 'root' })
export class LibraryAssistantService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/ai/library-assistant`;

  tools(): Observable<LibraryTool[]> {
    return this.http.get<LibraryTool[]>(`${this.api}/tools`);
  }

  /** Unsubscribing aborts the request; the API then stops the AI server's work. */
  ask(question: string, history: LibraryTurn[]): Observable<LibraryAnswer> {
    return this.http.post<LibraryAnswer>(`${this.api}/ask`, { question, history });
  }

  /** The most used saved questions (answered at once). */
  popular(): Observable<string[]> {
    return this.http.get<string[]>(`${this.api}/popular`);
  }

  /** Thumbs up (save as a rule with the tool and arguments shown) or down (remove). */
  feedback(answer: LibraryAnswer, helpful: boolean): Observable<unknown> {
    return this.http.post(`${this.api}/feedback`, { savedQuestionId: answer.savedQuestionId, helpful, tool: answer.tool, arguments: answer.arguments });
  }

  // ----- Admin -----

  saved(status: SavedQuestionStatus | '', search: string, skip: number, take: number): Observable<Paged<SavedQuestion>> {
    let params = new HttpParams().set('skip', skip).set('take', take);
    if (status) params = params.set('status', status);
    if (search.trim()) params = params.set('search', search.trim());
    return this.http.get<Paged<SavedQuestion>>(`${this.api}/saved`, { params });
  }

  /** tool: another look-up (e.g. to answer an unanswered question); omitted = keep. */
  saveSaved(id: number, status: Exclude<SavedQuestionStatus, 'Unanswered'>, args: Record<string, string>, tool?: string): Observable<unknown> {
    return this.http.put(`${this.api}/saved/${id}`, { status, arguments: args, tool: tool ?? null });
  }

  /** The question becomes one of a how-to guide's questions. */
  savedToGuide(id: number, guide: string): Observable<unknown> {
    return this.http.post(`${this.api}/saved/${id}/to-guide`, { guide });
  }

  synonyms(): Observable<Synonym[]> {
    return this.http.get<Synonym[]>(`${this.api}/synonyms`);
  }

  saveSynonym(word: string, kind: SynonymKind, value: string): Observable<unknown> {
    return this.http.put(`${this.api}/synonyms`, { word, kind, value });
  }

  deleteSynonym(id: number): Observable<unknown> {
    return this.http.delete(`${this.api}/synonyms/${id}`);
  }

  deleteSaved(id: number): Observable<unknown> {
    return this.http.delete(`${this.api}/saved/${id}`);
  }

  // ----- How-to guides (reading: every reader; changing: Admin) -----

  guides(): Observable<Guide[]> {
    return this.http.get<Guide[]>(`${this.api}/guides`);
  }

  saveGuide(code: string, guide: GuideInput): Observable<unknown> {
    return this.http.put(`${this.api}/guides/${encodeURIComponent(code)}`, guide);
  }

  deleteGuide(code: string): Observable<unknown> {
    return this.http.delete(`${this.api}/guides/${encodeURIComponent(code)}`);
  }
}
