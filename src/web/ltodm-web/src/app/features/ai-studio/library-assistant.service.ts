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
  /** rules: the phrase list (no AI). saved: a saved question (no AI). ai: the AI picked the tool. none: not answered. */
  source: 'rules' | 'saved' | 'look' | 'ai' | 'none';
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
}

export type SavedQuestionStatus = 'Recorded' | 'Rule' | 'Removed';

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

  saveSaved(id: number, status: SavedQuestionStatus, args: Record<string, string>): Observable<unknown> {
    return this.http.put(`${this.api}/saved/${id}`, { status, arguments: args });
  }

  deleteSaved(id: number): Observable<unknown> {
    return this.http.delete(`${this.api}/saved/${id}`);
  }
}
