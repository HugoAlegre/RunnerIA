import { HttpInterceptorFn } from '@angular/common/http';

const TOKEN_KEY = 'runnerAuthToken';

export function getRunnerAuthToken(): string {
  try {
    return sessionStorage.getItem(TOKEN_KEY) || '';
  } catch {
    return '';
  }
}

export function setRunnerAuthToken(token: string): void {
  try {
    if (token) sessionStorage.setItem(TOKEN_KEY, token);
    else sessionStorage.removeItem(TOKEN_KEY);
  } catch {
    /* ignore */
  }
}

export const runnerAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const token = getRunnerAuthToken();
  if (!token) return next(req);
  return next(
    req.clone({
      setHeaders: { 'X-Runner-Auth': token }
    })
  );
};
