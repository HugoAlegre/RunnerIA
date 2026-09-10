import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import {
  CatalogService,
  ModuloListaItem,
  PruebaListaItem
} from './catalog.service';

export type ConfirmFn = (
  title: string,
  body: string,
  confirmLabel: string,
  danger?: boolean
) => Promise<boolean>;

export type StatusFn = (msg: string, tipo: 'ok' | 'error' | 'warn' | 'info') => void;

/** Campos mínimos para eliminar/ocultar una prueba del catálogo. */
export type PruebaEliminable = Pick<PruebaListaItem, 'id' | 'titulo' | 'generada' | 'pruebaArchivo'>;

@Injectable({ providedIn: 'root' })
export class PruebasAdminService {
  private api = inject(ApiService);
  private catalog = inject(CatalogService);

  async refreshDeshacer(): Promise<{ disponible: boolean; hint: string }> {
    try {
      const d = await firstValueFrom(this.api.getPruebasDeshacer());
      return {
        disponible: !!d?.disponible,
        hint: d?.descripcion || d?.hint || ''
      };
    } catch {
      return { disponible: false, hint: '' };
    }
  }

  async deshacer(onStatus?: StatusFn): Promise<boolean> {
    try {
      const data = await firstValueFrom(this.api.postPruebasDeshacer());
      if (data?.ok === false) {
        onStatus?.(data.error || 'Nada para deshacer.', 'warn');
        return false;
      }
      onStatus?.(data.mensaje || 'Deshecho.', 'ok');
      await this.catalog.syncPruebas();
      return true;
    } catch (e: any) {
      onStatus?.(e?.message || 'Error al deshacer', 'error');
      return false;
    }
  }

  async eliminarModulo(
    m: ModuloListaItem,
    confirm: ConfirmFn,
    onStatus?: StatusFn
  ): Promise<boolean> {
    const n = m.pruebas.length;
    const title = m.fijo ? 'Ocultar del Runner' : 'Eliminar módulo';
    const body = m.fijo
      ? `¿Ocultar «${m.nombre}» del Runner?\n\nNo se borran los .feature del proyecto; solo deja de mostrarse en este navegador.`
      : n > 0
        ? `¿Eliminar «${m.nombre}» y sus ${n} prueba(s)?\n\nSe borran los archivos en Features/_pruebas.`
        : `¿Eliminar el módulo «${m.nombre}»?\n\nSi tenía pruebas asociadas, también se eliminan.`;
    const ok = await confirm(title, body, m.fijo ? 'Ocultar' : 'Eliminar', true);
    if (!ok) return false;
    const r = await this.catalog.eliminarModulo(m.id, n);
    if (!r.ok) {
      onStatus?.(r.error || 'No se pudo eliminar.', 'error');
      return false;
    }
    onStatus?.(r.mensaje || 'Módulo eliminado.', 'ok');
    await this.catalog.syncPruebas();
    return true;
  }

  async eliminarPrueba(
    p: PruebaEliminable,
    confirm: ConfirmFn,
    onStatus?: StatusFn
  ): Promise<boolean> {
    if (p.generada && p.pruebaArchivo) {
      const ok = await confirm(
        'Eliminar prueba',
        `¿Eliminar la prueba «${p.titulo}»?\n\nSe borra el archivo en Features/_pruebas.`,
        'Eliminar',
        true
      );
      if (!ok) return false;
      try {
        const data = await firstValueFrom(this.api.deletePruebasBorrador(p.pruebaArchivo));
        if (data?.ok === false) {
          onStatus?.(data.error || 'No se pudo eliminar.', 'error');
          return false;
        }
        onStatus?.(data.mensaje || 'Prueba eliminada.', 'ok');
        await this.catalog.syncPruebas();
        return true;
      } catch (e: any) {
        onStatus?.(e?.message || 'Error al eliminar', 'error');
        return false;
      }
    }
    const okSuite = await confirm(
      'Quitar del Runner',
      `¿Eliminar «${p.titulo}» del Runner?\n\nEl .feature del proyecto NO se borra; solo deja de mostrarse acá.`,
      'Quitar',
      true
    );
    if (!okSuite) return false;
    this.catalog.ocultarEscenarioSuite(p.id);
    onStatus?.('Escenario eliminado del Runner.', 'ok');
    await this.catalog.syncPruebas();
    return true;
  }
}
