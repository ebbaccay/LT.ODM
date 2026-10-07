export interface UserProfile {
  userId: number;
  userName: string;
  email: string;
  displayName: string;
  roles: string[];
  mustChangePassword: boolean;
  /** TMS user group ('FTY' = factory user). */
  userGroup?: string | null;
  /** TMS location / factory code. */
  location?: string | null;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresUtc: string;
  user: UserProfile;
}

export interface PasswordPolicy {
  minLength: number;
  maxLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireSymbol: boolean;
  historyCount: number;
}

/** Same defaults as the API (Auth:Password); replaced by GET /api/v1/auth/password-policy. */
export const DEFAULT_PASSWORD_POLICY: PasswordPolicy = {
  minLength: 12,
  maxLength: 128,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireSymbol: true,
  historyCount: 5,
};

/** ASP.NET ProblemDetails / ValidationProblemDetails. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
