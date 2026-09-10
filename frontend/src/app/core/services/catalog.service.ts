import { Injectable, computed, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CATEGORIAS, ESCENARIOS } from '../catalog-data';
import { Categoria, Escenario } from '../models';
import { ApiService } from './api.service';

const LS_ALIAS = 'sot-runner-modulos-alias';
const LS_OCULTOS = 'sot-runner-modulos-ocultos';
const LS_SUITE_OCULTOS = 'suiteOcultos';
const RELEASE9_IDS_FIJOS = [
  'r9-SC-161',
  'r9-SC-402',
  'r9-SC-414',
  'r9-SC-461',
  'r9-SC-471',
  'r9-SC-472',
  'r9-SC-476',
  'r9-SC-476-qa',
  'r9-SC-484',
  'r9-SC-485',
  'r9-SC-492',
  'r9-SC-493',
  'r9-todo'
] as const;

export interface PruebaListaItem {
  id: string;
  titulo: string;
  corto: string;
  generada: boolean;
  pruebaArchivo?: string;
  tag?: string;
  code: string;
}

export interface ModuloListaItem {
  id: string;
  nombre: string;
  fijo: boolean;
  pruebas: PruebaListaItem[];
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly baseCategorias = structuredClone(CATEGORIAS) as Categoria[];
  private readonly baseEscenarios = structuredClone(ESCENARIOS) as Record<string, Escenario>;

  readonly categorias = signal<Categoria[]>(structuredClone(this.baseCategorias));
  readonly escenarios = signal<Record<string, Escenario>>(structuredClone(this.baseEscenarios));
  readonly categoriaId = signal(this.categorias()[0]?.id || 'regresion');
  readonly escenarioId = signal<string | null>(this.categorias()[0]?.escenarios?.[0] || null);

  readonly categoriasVisibles = computed(() =>
    this.categorias().filter((c) => c?.id && !this.moduloEstaOculto(c.id))
  );

  constructor(private api: ApiService) {
    this.restaurarSuiteSiModuloVacio();
    this.aplicarAliasACategorias();
  }

  esTesting(cat?: Categoria | { id?: string; testing?: boolean } | null): boolean {
    if (!cat) return false;
    if (cat.testing) return true;
    const id = String(cat.id || '');
    return id.startsWith('grp-') || id === 'asistente-qa-pruebas';
  }

  categoriaActual(): Categoria | undefined {
    const id = this.resolveModuloId(this.categoriaId());
    return this.categorias().find((c) => c.id === id);
  }

  escenarioActual(): Escenario | null {
    const id = this.escenarioId();
    if (!id) return null;
    const vis = this.escenariosVisibles();
    if (!vis.includes(id)) return null;
    return this.escenarios()[id] || null;
  }

  escenariosVisibles(cat?: Categoria): string[] {
    const c = cat || this.categoriaActual();
    if (!c) return [];
    const map = this.escenarios();
    const ocultos = this.suiteOcultosLeer();
    return (c.escenarios || []).filter((id) => !!map[id] && !ocultos.includes(String(id)));
  }

  /** Escenario activo pertenece al módulo activo. */
  escenarioPerteneceAModuloActivo(escId: string | null | undefined): boolean {
    if (!escId) return false;
    return this.escenariosVisibles().includes(escId);
  }

  /** Pruebas @Prueba asignadas a Release 9 (módulo fijo o grp-release-9). */
  esPruebaRelease9(e: Escenario | null | undefined): boolean {
    if (!e) return false;
    const gid = String(e.grupoId || '');
    if (
      gid === 'grp-release-9' ||
      gid === 'release-9' ||
      gid.startsWith('grp-r9-') ||
      gid === 'grp-parametria-sc161' ||
      this.resolverModuloUiId(gid) === 'release-9'
    )
      return true;
    const arch = String(e.pruebaArchivo || '').replace(/\\/g, '/');
    if (arch.startsWith('Release-9/') || arch.includes('/Release-9/')) return true;
    const ruta = String(e.rutaRelativa || '').replace(/\\/g, '/');
    if (ruta.startsWith('Release-9/') || ruta.includes('/Release-9/')) return true;
    const tag = String(e.tag || '');
    if (/\b@Release9\b/i.test(tag) || /\bRelease9\b/i.test(tag)) return true;
    return false;
  }

  /** Total regresión desde API (sin Release 9); fallback local si offline. */
  readonly regresionPruebasApi = signal<number | null>(null);

  readonly countPruebasRegresion = computed(() => {
    const api = this.regresionPruebasApi();
    if (api !== null && api >= 0) return api;
    const ocultos = new Set(this.suiteOcultosLeer());
    return Object.entries(this.escenarios()).filter(([id, e]) => {
      if (!e?.generada) return false;
      if (ocultos.has(id)) return false;
      if (this.esPruebaRelease9(e)) return false;
      return true;
    }).length;
  });

  puedeGestionarModulo(cat?: Categoria | null): boolean {
    const c = cat || this.categoriaActual();
    return !!(c && c.id !== 'cat-regresion-pruebas' && !this.moduloEstaOculto(c.id));
  }

  seleccionarCategoria(id: string): void {
    this.activarModulo(id);
  }

  /**
   * Activa un módulo y deja un escenario coherente (siempre del mismo módulo).
   * Acepta ids grp-* del backend (grp-intercaja → intercaja).
   */
  activarModulo(catId: string, escenarioPreferido?: string | null): boolean {
    const id = this.resolveModuloId(catId);
    const cat = this.categorias().find((c) => c.id === id);
    if (!cat) return false;

    this.categoriaId.set(id);
    const vis = this.escenariosVisibles(cat);
    let esc: string | null = null;

    const pref = escenarioPreferido?.trim();
    if (pref && vis.includes(pref)) {
      esc = pref;
    } else {
      const cur = this.escenarioId();
      if (cur && vis.includes(cur)) esc = cur;
      else esc = vis[0] || null;
    }

    this.escenarioId.set(esc);
    return true;
  }

  /** Normaliza id de módulo (grp-intercaja → intercaja si existe el fijo). */
  resolveModuloId(catId: string): string {
    const gid = String(catId || '').trim();
    if (!gid) return '';
    if (this.categorias().some((c) => c.id === gid)) return gid;
    const ui = this.resolverModuloUiId(gid);
    if (this.categorias().some((c) => c.id === ui)) return ui;
    return gid;
  }

  seleccionarEscenario(id: string | null): void {
    const vis = this.escenariosVisibles();
    if (id && !vis.includes(id)) {
      this.escenarioId.set(vis[0] || null);
      return;
    }
    this.escenarioId.set(id);
  }

  /** Copia descripcion/sub de módulos fijos desde catalog-data. */
  private aplicarDescripcionesModulosBase(cats: Categoria[]): void {
    for (const c of cats) {
      const base = this.baseCategorias.find((b) => b.id === c.id);
      if (!base) continue;
      if (base.descripcion) c.descripcion = base.descripcion;
      if (base.sub && !c.sub) c.sub = base.sub;
    }
  }

  /** Lista completa para «Módulos y pruebas» (fijos + testing). */
  listaModulosCompleta(): ModuloListaItem[] {
    const cats = this.categorias();
    const escMap = this.escenarios();
    const out: ModuloListaItem[] = [];
    const vistos = new Set<string>();
    const fijos = this.idsModulosFijos();

    for (const c of cats) {
      if (!c?.id || c.id === 'cat-regresion-pruebas' || this.moduloEstaOculto(c.id)) continue;
      // Evitar duplicados grp-{fijo} si quedaran en el catálogo
      const uiId = this.resolverModuloUiId(c.id);
      if (uiId !== c.id && fijos.has(uiId)) continue;
      if (vistos.has(c.id)) continue;
      vistos.add(c.id);
      const ids = this.escenariosVisibles(c);
      const pruebas: PruebaListaItem[] = [];
      for (const id of ids) {
        const e = escMap[id];
        if (!e) continue;
        pruebas.push(this.toPruebaItem(id, e));
      }
      out.push({
        id: c.id,
        nombre: this.moduloAliasDe(c.id, c.nombre),
        fijo: !this.esTesting(c),
        pruebas
      });
    }
    // Fijos en orden de suite; testing A→Z. Casos de cada módulo A→Z por título.
    const listaFijos = out.filter((m) => m.fijo);
    const listaTesting = out
      .filter((m) => !m.fijo)
      .sort((a, b) => this.compararTexto(a.nombre, b.nombre));
    for (const m of [...listaFijos, ...listaTesting]) {
      m.pruebas.sort((a, b) => this.compararTexto(a.titulo, b.titulo) || this.compararTexto(a.id, b.id));
    }
    return [...listaFijos, ...listaTesting];
  }

  /** Ids de módulos de suite fijos (no testing). */
  private idsModulosFijos(): Set<string> {
    return new Set(
      this.baseCategorias
        .filter((c) => c?.id && !this.esTesting(c) && !String(c.id).startsWith('grp-'))
        .map((c) => String(c.id))
    );
  }

  /**
   * El backend crea grupos `grp-{runnerId}` al asignar prueba a un módulo fijo.
   * En UI los fusionamos al módulo fijo para no duplicar (ej. dos «Intercaja»).
   */
  private resolverModuloUiId(grupoId: string): string {
    const gid = String(grupoId || '').trim();
    if (!gid) return 'grp-sin-clasificar';
    // Release 9: un solo módulo fijo (grp-release-9, grp-r9-*, grp-parametria-sc161 legacy).
    if (gid === 'grp-release-9' || gid === 'grp-parametria-sc161' || gid.startsWith('grp-r9-'))
      return 'release-9';
    const fijos = this.idsModulosFijos();
    if (fijos.has(gid)) return gid;
    if (gid.startsWith('grp-')) {
      const maybe = gid.slice(4);
      if (fijos.has(maybe)) return maybe;
    }
    return gid;
  }

  async syncPruebas(): Promise<void> {
    try {
      const data = await firstValueFrom(this.api.getPruebasEscenarios());
      if (typeof data.regresionPruebas === 'number') {
        this.regresionPruebasApi.set(data.regresionPruebas);
      }
      const fijos = this.idsModulosFijos();
      const cats = this.baseCategorias
        .filter((c) => !this.esTesting(c) && !String(c.id).startsWith('grp-'))
        .map((c) => ({ ...c, escenarios: [...(c.escenarios || [])] }));
      const esc: Record<string, Escenario> = { ...structuredClone(this.baseEscenarios) };

      const apiTieneSinClasificar = (data.grupos || []).some(
        (g: { id?: string }) => g?.id === 'grp-sin-clasificar'
      );

      for (const g of data.grupos || []) {
        if (!g?.id) continue;
        const uiId = this.resolverModuloUiId(g.id);
        // No listar como módulo aparte los espejos grp-{fijo}
        if (uiId !== g.id && fijos.has(uiId)) continue;
        if (!cats.some((c) => c.id === g.id)) {
          cats.push({
            id: g.id,
            nombre: g.nombre || g.id,
            sub: g.sub || 'Grupo de testing',
            descripcion: g.sub || 'Grupo de testing generado.',
            escenarios: [],
            testing: true
          });
        }
      }

      for (const b of data.casos || []) {
        if (!b?.id) continue;
        const gidRaw = b.grupoId || 'grp-sin-clasificar';
        const gid = this.resolverModuloUiId(gidRaw);
        // No revivir «Testing (sin clasificar)» si el API ya no lo tiene (fue eliminado).
        if (gid === 'grp-sin-clasificar' && !apiTieneSinClasificar) continue;
        let cat = cats.find((c) => c.id === gid);
        if (!cat) {
          cat = {
            id: gid,
            nombre: 'Testing',
            sub: 'Pruebas generadas',
            descripcion: 'Pruebas generadas con el asistente de QA.',
            escenarios: [],
            testing: true
          };
          cats.push(cat);
        }
        if (!cat.escenarios.includes(b.id)) cat.escenarios.push(b.id);
        esc[b.id] = {
          num: b.id,
          titulo: String(b.titulo || b.nombre || b.id).replace(/^Prueba\s*[—\-–:]?\s*/i, ''),
          corto: b.corto || '',
          descripcion: b.descripcion || b.corto || '',
          tag: b.tag || 'prueba',
          resultado: b.resultado || 'Cumplir resultado esperado',
          script: b.script || 'run-PruebaFeature.ps1',
          extraArgs: b.extraArgs || '',
          prerequisitos: b.prerequisitos || ['Configuración SOT cargada'],
          evidencias: b.evidencias || ['Informe HTML Extent'],
          generada: true,
          pruebaArchivo: b.nombre || b.pruebaArchivo,
          rutaRelativa: b.rutaRelativa,
          grupoId: gidRaw
        };
      }

      // Si el usuario lo borró (OmitirSinClasificar), no lo reinsertamos.
      const sinCat = cats.find((c) => c.id === 'grp-sin-clasificar');
      if (!apiTieneSinClasificar && sinCat) {
        const idx = cats.findIndex((c) => c.id === 'grp-sin-clasificar');
        if (idx >= 0) cats.splice(idx, 1);
      }

      // Mantener la lista completa en la categoría (como legacy).
      // La visibilidad se filtra en escenariosVisibles() vía suiteOcultos.
      for (let i = cats.length - 1; i >= 0; i--) {
        const c = cats[i];
        if (c.id === 'release-9') continue;
        if (this.resolverModuloUiId(c.id) === 'release-9') cats.splice(i, 1);
      }
      this.bootstrapRelease9Modulo(cats, esc, data.casos || []);
      this.restaurarSuiteSobre(cats, esc);
      this.ordenarEscenariosRelease9(cats);
      this.ordenarModulosYCasos(cats, esc);
      this.aplicarDescripcionesModulosBase(cats);
      this.categorias.set(cats);
      this.escenarios.set(esc);
      this.aplicarAliasACategorias();

      const visibles = this.categoriasVisibles();
      const prevCat = this.categoriaId();
      const prevEsc = this.escenarioId();
      if (!visibles.some((c) => c.id === this.resolveModuloId(prevCat))) {
        this.activarModulo(visibles[0]?.id || cats[0]?.id || 'regresion');
      } else {
        this.activarModulo(prevCat, prevEsc);
      }
    } catch {
      /* servidor offline: conservar catálogo local */
      this.restaurarSuiteSiModuloVacio();
      this.aplicarAliasACategorias();
    }
  }

  /** Fusiona escenarios de suite base en módulos fijos visibles. */
  private restaurarSuiteSiModuloVacio(): void {
    const cats = this.categorias();
    const esc = this.escenarios();
    if (this.restaurarSuiteSobre(cats, esc)) {
      this.aplicarDescripcionesModulosBase(cats);
      this.categorias.set([...cats]);
    }
  }

  /** Igual que restaurarSuiteSiModuloVacio pero sobre arrays en construcción. */
  private restaurarSuiteSobre(cats: Categoria[], escMap: Record<string, Escenario>): boolean {
    let changed = false;
    for (const c of cats) {
      if (this.esTesting(c) || c.id === 'cat-regresion-pruebas') continue;
      if (this.moduloEstaOculto(c.id)) continue;
      const base = this.baseCategorias.find((b) => b.id === c.id);
      if (!base?.escenarios?.length) continue;

      const pruebaIds = (c.escenarios || []).filter((id) => {
        const e = escMap[id];
        return !!e?.generada || !base.escenarios.includes(id);
      });
      const merged = [...base.escenarios];
      for (const id of pruebaIds) {
        if (!merged.includes(id)) merged.push(id);
      }
      if (JSON.stringify(merged) !== JSON.stringify(c.escenarios || [])) {
        c.escenarios = merged;
        changed = true;
      }
    }
    return changed;
  }

  /** Release 9: SC-161 primero, tickets por número, «Correr todo» al final. */
  private ordenarEscenariosRelease9(cats: Categoria[]): void {
    const cat = cats.find((c) => c.id === 'release-9');
    if (!cat?.escenarios?.length) return;

    const ids = [...cat.escenarios];
    const fijosFin = ['r9-todo'];
    const fijosInicio = ids
      .filter((id) => id.startsWith('r9-SC-') && id !== 'r9-todo')
      .sort((a, b) => {
        const na = parseInt(a.replace(/\D/g, ''), 10) || 9999;
        const nb = parseInt(b.replace(/\D/g, ''), 10) || 9999;
        return na - nb || this.compararTexto(a, b);
      });
    const medio = ids.filter((id) => !fijosInicio.includes(id) && !fijosFin.includes(id));
    medio.sort((a, b) => {
      const pick = (s: string) => {
        const m = s.match(/SC[_-]?(\d+)/i);
        return m ? parseInt(m[1], 10) : 9999;
      };
      const pa = pick(a);
      const pb = pick(b);
      return pa !== pb ? pa - pb : this.compararTexto(a, b);
    });
    cat.escenarios = [
      ...fijosInicio,
      ...medio,
      ...fijosFin.filter((id) => ids.includes(id))
    ];
  }

  /** Módulos testing A→Z; en fijos: suite primero, generados A→Z. */
  private ordenarModulosYCasos(cats: Categoria[], escMap: Record<string, Escenario>): void {
    const fijos = this.idsModulosFijos();
    const fijosOrden = cats.filter((c) => fijos.has(c.id) || c.id === 'cat-regresion-pruebas');
    const testing = cats
      .filter((c) => !fijos.has(c.id) && c.id !== 'cat-regresion-pruebas')
      .sort((a, b) => this.compararTexto(a.nombre || a.id, b.nombre || b.id));

    cats.length = 0;
    cats.push(...fijosOrden, ...testing);

    for (const c of cats) {
      if (!c.escenarios?.length) continue;
      if (c.id === 'release-9') continue;
      if (c.id === 'cat-regresion-pruebas') continue;

      const base = this.baseCategorias.find((b) => b.id === c.id);
      if (base?.escenarios?.length && !this.esTesting(c)) {
        const suiteIds = base.escenarios.filter((id) => c.escenarios.includes(id));
        const generados = c.escenarios
          .filter((id) => !base.escenarios.includes(id))
          .sort((a, b) => this.compararCasos(a, b, escMap));
        c.escenarios = [...suiteIds, ...generados];
      } else {
        c.escenarios = [...c.escenarios].sort((a, b) => this.compararCasos(a, b, escMap));
      }
    }
  }

  private compararCasos(a: string, b: string, escMap: Record<string, Escenario>): number {
    const ta = String(escMap[a]?.titulo || a);
    const tb = String(escMap[b]?.titulo || b);
    return this.compararTexto(ta, tb) || this.compararTexto(a, b);
  }

  private compararTexto(a: string, b: string): number {
    return String(a || '').localeCompare(String(b || ''), 'es', { numeric: true, sensitivity: 'base' });
  }

  /** Completa módulo Release 9 y escenarios fijos desde catalog-data. */
  private bootstrapRelease9Modulo(
    cats: Categoria[],
    esc: Record<string, Escenario>,
    casos: Array<{ id?: string; grupoId?: string }>
  ): void {
    for (const id of RELEASE9_IDS_FIJOS) {
      if (!esc[id] && this.baseEscenarios[id]) esc[id] = structuredClone(this.baseEscenarios[id]);
    }

    let cat = cats.find((c) => c.id === 'release-9');
    if (!cat) {
      cat = {
        id: 'release-9',
        nombre: 'Release 9',
        sub: 'Automatización en Runner; T.O. y TimeOut QA manual fuera del Runner',
        escenarios: []
      };
      cats.push(cat);
    }

    const escenariosCat = new Set(cat.escenarios || []);
    for (const id of RELEASE9_IDS_FIJOS) escenariosCat.add(id);

    const tieneR9 = casos.some((b) => this.resolverModuloUiId(b.grupoId || '') === 'release-9');
    if (tieneR9 || escenariosCat.size > 2) {
      for (const id of escenariosCat) {
        if (!cat.escenarios!.includes(id)) cat.escenarios!.push(id);
      }
    }
  }

  async renombrarModulo(id: string, nuevoNombre: string): Promise<{ ok: boolean; error?: string }> {
    const nom = String(nuevoNombre || '').trim();
    if (!nom) return { ok: false, error: 'El nombre no puede quedar vacío.' };
    const cat = this.categorias().find((c) => c.id === id);
    if (!cat || id === 'cat-regresion-pruebas') return { ok: false, error: 'Módulo no válido.' };

    if (this.esTesting(cat)) {
      try {
        const data = await firstValueFrom(this.api.putPruebasGrupo(id, { nombre: nom }));
        if (data?.ok === false) return { ok: false, error: data.error || 'No se pudo renombrar.' };
      } catch (e: any) {
        return { ok: false, error: e?.message || 'Error al renombrar.' };
      }
    }

    this.moduloAliasSet(id, nom);
    this.categorias.update((list) =>
      list.map((c) => (c.id === id ? { ...c, nombre: nom } : c))
    );
    return { ok: true };
  }

  async eliminarModulo(id: string, cantidadCasos = 0): Promise<{ ok: boolean; error?: string; mensaje?: string }> {
    if (!id || id === 'cat-regresion-pruebas') {
      return { ok: false, error: 'La regresión de pruebas no se elimina.' };
    }
    const cat = this.categorias().find((c) => c.id === id);

    if (!this.esTesting(cat || { id })) {
      // Fijo: ocultar en este navegador
      if (cat?.escenarios) {
        for (const escId of cat.escenarios) {
          const e = this.escenarios()[escId];
          if (e && !e.generada) this.suiteOcultarEscenario(escId);
        }
      }
      this.moduloOcultar(id);
      if (this.categoriaId() === id) {
        const vis = this.categoriasVisibles();
        this.seleccionarCategoria(vis[0]?.id || 'regresion');
      }
      await this.syncPruebas();
      return { ok: true, mensaje: 'Módulo oculto del Runner.' };
    }

    try {
      // Siempre borrar casos del grupo al eliminar el módulo (confirmado en UI).
      const data = await firstValueFrom(this.api.deletePruebasGrupo(id, true));
      if (data?.ok === false) return { ok: false, error: data.error || 'No se pudo eliminar.' };
      if (this.categoriaId() === id) {
        this.seleccionarCategoria(this.categoriasVisibles().find((c) => c.id !== id)?.id || 'regresion');
      }
      await this.syncPruebas();
      // Por si el API aún lo devolviera vacío, sacarlo de la lista local
      this.categorias.update((list) => list.filter((c) => c.id !== id));
      return { ok: true, mensaje: data?.mensaje || 'Módulo eliminado.' };
    } catch (e: any) {
      const status = e?.status;
      const msg = e?.error?.error || e?.message || 'Error al eliminar.';
      if (status === 404) {
        this.categorias.update((list) => list.filter((c) => c.id !== id));
        if (this.categoriaId() === id) {
          this.seleccionarCategoria(this.categoriasVisibles()[0]?.id || 'regresion');
        }
        await this.syncPruebas();
        this.categorias.update((list) => list.filter((c) => c.id !== id));
        return { ok: true, mensaje: 'Módulo eliminado del Runner.' };
      }
      return { ok: false, error: msg };
    }
  }

  ocultarEscenarioSuite(id: string): void {
    this.suiteOcultarEscenario(id);
    if (this.escenarioId() === id) {
      const vis = this.escenariosVisibles();
      this.escenarioId.set(vis[0] || null);
    }
  }

  private toPruebaItem(id: string, e: Escenario): PruebaListaItem {
    return {
      id,
      titulo: e.titulo || id,
      corto: e.corto || e.descripcion || '',
      generada: !!e.generada,
      pruebaArchivo: e.pruebaArchivo,
      tag: e.tag,
      code: e.generada ? e.pruebaArchivo || id : e.tag || id
    };
  }

  // —— localStorage helpers ——

  private aliasLeer(): Record<string, string> {
    try {
      return JSON.parse(localStorage.getItem(LS_ALIAS) || '{}') || {};
    } catch {
      return {};
    }
  }

  moduloAliasDe(id: string, fallback?: string): string {
    const n = this.aliasLeer()[String(id || '')];
    return (n && String(n).trim()) || fallback || id;
  }

  private moduloAliasSet(id: string, nombre: string): void {
    if (!id) return;
    const m = this.aliasLeer();
    const n = String(nombre || '').trim();
    if (n) m[id] = n;
    else delete m[id];
    try {
      localStorage.setItem(LS_ALIAS, JSON.stringify(m));
    } catch {
      /* ignore */
    }
  }

  private ocultosLeer(): string[] {
    try {
      return JSON.parse(localStorage.getItem(LS_OCULTOS) || '[]') || [];
    } catch {
      return [];
    }
  }

  moduloEstaOculto(id: string): boolean {
    return this.ocultosLeer().includes(String(id));
  }

  private moduloOcultar(id: string): void {
    const a = this.ocultosLeer();
    const s = String(id);
    if (!a.includes(s)) {
      a.push(s);
      try {
        localStorage.setItem(LS_OCULTOS, JSON.stringify(a));
      } catch {
        /* ignore */
      }
    }
  }

  private suiteOcultosLeer(): string[] {
    try {
      return JSON.parse(localStorage.getItem(LS_SUITE_OCULTOS) || '[]') || [];
    } catch {
      return [];
    }
  }

  private suiteOcultarEscenario(id: string): void {
    const a = this.suiteOcultosLeer();
    const s = String(id);
    if (!a.includes(s)) {
      a.push(s);
      try {
        localStorage.setItem(LS_SUITE_OCULTOS, JSON.stringify(a));
      } catch {
        /* ignore */
      }
    }
  }

  private aplicarAliasACategorias(): void {
    this.limpiarAliasTitulosAntiguos();
    this.categorias.update((list) =>
      list.map((c) => ({
        ...c,
        nombre: this.moduloAliasDe(c.id, c.nombre)
      }))
    );
  }

  /** Quita aliases locales que aún tienen el título largo anterior. */
  private limpiarAliasTitulosAntiguos(): void {
    const viejos: Record<string, string[]> = {
      regresion: ['Regresión Alta + Intercaja'],
      cheques: ['Depósito e Interdepósito de cheques'],
      'parametria-sc161': ['Parametría Relación Transacción y Perfil Contable']
    };
    const m = this.aliasLeer();
    let changed = false;
    for (const [id, nombres] of Object.entries(viejos)) {
      const actual = String(m[id] || '').trim();
      if (actual && nombres.some((n) => n.toLowerCase() === actual.toLowerCase())) {
        delete m[id];
        changed = true;
      }
    }
    if (!changed) return;
    try {
      localStorage.setItem(LS_ALIAS, JSON.stringify(m));
    } catch {
      /* ignore */
    }
  }
}
