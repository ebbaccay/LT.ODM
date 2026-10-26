import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable, catchError, map, of, switchMap, takeWhile, timer } from 'rxjs';

// AI jobs (/api/v1/ai/jobs): the API runs slow AI work (renders, summaries, explanations, readers) in the background and
// answers the request with a job id at once. run() follows the job until it finishes and gives the answer as the request
// used to, so pages and services barely change. Leaving the page does not stop a job; only cancel() does.

export type AiJobStatus = 'Queued' | 'Running' | 'Done' | 'Failed' | 'Cancelled' | 'Interrupted';

/** How far a running task has got: queued at the AI server (ahead = calls before it), sent, or writing (text answers). */
export interface AiJobProgress {
  phase: 'queued' | 'waiting' | 'writing';
  words: number;
  seconds: number;
  ahead?: number | null;
}

export interface AiJob<T = unknown> {
  jobId: number;
  kind: string;
  lane: 'text' | 'image';
  status: AiJobStatus;
  ownerName: string;
  taskKey: string | null;
  label: string | null;
  url: string | null;
  error: string | null;
  /** The HTTP status the request would have answered with (502 AI failure, 404 not found, ...). */
  errorStatus: number | null;
  seen: boolean;
  createdUtc: string;
  startedUtc: string | null;
  finishedUtc: string | null;
  cancelledBy: string | null;
  result: T | null;
  progress: AiJobProgress | null;
}

/** One AI server's load now (AI call limits): calls running out of its limit, and calls waiting. */
export interface AiServerLoad {
  name: string;
  limit: number;
  running: number;
  waitingInteractive: number;
  waitingBackground: number;
}

export interface AiJobPage {
  items: AiJob[];
  total: number;
  keepDays: number;
  servers: AiServerLoad[] | null;
}

/** What the AI task tray tells the API about a job so it can show it again after a reload. */
export interface AiJobTask {
  key: string;
  label: string;
  url: string;
}

/** Callbacks for one run, registered under the tray's id (see describe). */
interface Follow {
  task?: AiJobTask;
  started?: (jobId: number) => void;
  progress?: (progress: AiJobProgress) => void;
}

/** Seconds between polls: often enough to look alive, far below the rate limit. */
const POLL_MS = 2000;
/** Polls that may fail in a row (network blips, a token refresh) before the job is given up on this page. */
const MAX_POLL_ERRORS = 30;

export const isActive = (status: AiJobStatus) => status === 'Queued' || status === 'Running';

@Injectable({ providedIn: 'root' })
export class AiJobsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/ai/jobs`;
  private readonly adminApi = `${environment.apiBaseUrl}/api/v1/admin/ai/jobs`;
  private readonly follows = new Map<string, Follow>();

  /** The tray: what to send with the request started under this id, and what to call back as the job goes. */
  describe(id: string, follow: Follow): void {
    this.follows.set(id, follow);
  }

  /** Request options for an endpoint that queues a job (the tray's key, label and page, when it describes the run). */
  options(id?: string): { headers?: HttpHeaders } {
    const task = id ? this.follows.get(id)?.task : undefined;
    if (!task) return {};
    return {
      headers: new HttpHeaders({
        'X-Ai-Task-Key': encodeURIComponent(task.key),
        'X-Ai-Task-Label': encodeURIComponent(task.label),
        'X-Ai-Task-Url': encodeURIComponent(task.url),
      }),
    };
  }

  /** Starts the job (the request answers 202 with its id) and gives its answer once it has finished. */
  run<T>(start: Observable<{ jobId: number }>, id?: string): Observable<T> {
    const follow = id ? this.follows.get(id) : undefined;
    if (id) this.follows.delete(id);
    return start.pipe(
      switchMap(({ jobId }) => {
        follow?.started?.(jobId);
        return this.follow<T>(jobId, follow?.progress);
      }),
    );
  }

  /** Follows a job already running (e.g. found again after a reload) until it finishes. */
  follow<T>(jobId: number, progress?: (p: AiJobProgress) => void): Observable<T> {
    let errors = 0;
    return timer(500, POLL_MS).pipe(
      switchMap(() =>
        this.get<T>(jobId).pipe(
          map((job) => ((errors = 0), job)),
          catchError((e) => {
            if (e instanceof HttpErrorResponse && e.status === 404) throw e;
            if (++errors >= MAX_POLL_ERRORS) throw e;
            return of(null);
          }),
        ),
      ),
      takeWhile((job) => !job || isActive(job.status), true),
      map((job) => {
        if (!job) return null;
        if (job.progress) progress?.(job.progress);
        if (isActive(job.status)) return null;
        if (job.status === 'Done') return { value: job.result as T };
        throw AiJobsService.failure(job);
      }),
      switchMap((answer) => (answer ? of(answer.value) : [])),
    );
  }

  /** The caller's jobs for the tray: running, or finished and not yet shown. */
  mine(): Observable<AiJob[]> {
    return this.http.get<AiJob[]>(this.api);
  }

  get<T>(jobId: number): Observable<AiJob<T>> {
    return this.http.get<AiJob<T>>(`${this.api}/${jobId}`);
  }

  cancel(jobId: number): Observable<unknown> {
    return this.http.post(`${this.api}/${jobId}/cancel`, null);
  }

  seen(jobId: number): Observable<unknown> {
    return this.http.post(`${this.api}/${jobId}/seen`, null);
  }

  /** Admin: every job, newest first; status 'Active' = queued or running. */
  list(status: string, skip: number, take: number): Observable<AiJobPage> {
    let params = new HttpParams().set('skip', skip).set('take', take);
    if (status) params = params.set('status', status);
    return this.http.get<AiJobPage>(this.adminApi, { params });
  }

  saveKeepDays(days: number): Observable<unknown> {
    return this.http.put(`${this.adminApi}/keep-days`, { days });
  }

  /** A finished job that did not succeed, as the error its request would have given (problemMessage reads it). */
  static failure(job: AiJob): HttpErrorResponse {
    const status = job.status === 'Failed' ? (job.errorStatus ?? 500) : 409;
    return new HttpErrorResponse({ status, statusText: job.status, error: { title: job.error ?? job.status } });
  }
}
