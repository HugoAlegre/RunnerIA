import { Injectable, signal } from '@angular/core';

export type ToastTipo = 'ok' | 'error' | 'warn' | 'info';

export interface ToastItem {
  id: number;
  mensaje: string;
  tipo: ToastTipo;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly items = signal<ToastItem[]>([]);
  private seq = 0;

  show(mensaje: string, tipo: ToastTipo = 'info', ms = 3800): void {
    const id = ++this.seq;
    this.items.update((list) => [...list, { id, mensaje, tipo }]);
    window.setTimeout(() => this.dismiss(id), ms);
  }

  ok(mensaje: string): void {
    this.show(mensaje, 'ok');
  }

  error(mensaje: string): void {
    this.show(mensaje, 'error', 5200);
  }

  warn(mensaje: string): void {
    this.show(mensaje, 'warn', 4500);
  }

  info(mensaje: string): void {
    this.show(mensaje, 'info');
  }

  dismiss(id: number): void {
    this.items.update((list) => list.filter((t) => t.id !== id));
  }
}
