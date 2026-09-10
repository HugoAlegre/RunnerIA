import { Injectable, signal } from '@angular/core';

const GATE_KEY = 'sot-runner-proyecto-gate-ok';

/** Sesión Runner expirada (API reiniciada o token inválido). */
@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  readonly requiereLogin = signal(false);
  /** Usuario eligió proyecto en esta sesión de navegador (tras PIN). */
  readonly proyectoGateOk = signal(this.leerGateOk());

  marcarSesionExpirada(): void {
    this.requiereLogin.set(true);
    this.limpiarProyectoGate();
  }

  limpiarRequiereLogin(): void {
    this.requiereLogin.set(false);
  }

  marcarProyectoGateOk(): void {
    try {
      sessionStorage.setItem(GATE_KEY, '1');
    } catch {
      /* ignore */
    }
    this.proyectoGateOk.set(true);
  }

  limpiarProyectoGate(): void {
    try {
      sessionStorage.removeItem(GATE_KEY);
    } catch {
      /* ignore */
    }
    this.proyectoGateOk.set(false);
  }

  private leerGateOk(): boolean {
    try {
      return sessionStorage.getItem(GATE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
