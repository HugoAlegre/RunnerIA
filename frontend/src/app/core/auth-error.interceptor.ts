import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { setRunnerAuthToken } from './auth.interceptor';
import { AuthSessionService } from './services/auth-session.service';

/** Si la API pide sesión (reinicio del servidor), pedir PIN sin recargar la página entera. */
export const runnerAuthErrorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && err.error?.authRequired) {
        setRunnerAuthToken('');
        inject(AuthSessionService).marcarSesionExpirada();
      }
      return throwError(() => err);
    })
  );
};
