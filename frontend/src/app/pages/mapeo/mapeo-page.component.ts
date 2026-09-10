import { Component, inject, signal, OnInit, OnDestroy, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService, MapeoEventoUi } from '../../core/services/api.service';

type PreviewTab = 'resumen' | 'json' | 'gherkin';

@Component({
  selector: 'app-mapeo-page',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './mapeo-page.component.html',
  styleUrl: './mapeo-page.component.scss'
})
export class MapeoPageComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  url = '';
  headless = false;
  loginAutomatico = true;
  proyectoId = signal('');
  proyectoNombre = signal('');
  plantilla = signal<'sot' | 'generico'>('generico');
  usuarioConfig = signal('');
  sucursalObjetivo = signal('');

  readonly activa = signal(false);
  sesionId = signal('');
  urlActual = signal('');
  avisosLogin = signal<string[]>([]);
  busy = signal(false);
  error = signal('');
  info = signal('');
  eventos = signal<MapeoEventoUi[]>([]);
  ultimoOrden = signal(0);

  guardadoEn = signal('');
  guardadoAbsoluto = signal('');
  previewTab = signal<PreviewTab>('resumen');
  gherkinPreview = signal('');
  jsonPreview = signal('');
  eventoExpandido = signal<number | null>(null);

  consultaManual = '';
  xpathManual = '';
  hitsManual = signal<
    Array<{ repoId?: string; archivo?: string; linea?: number; fragmento?: string; termino?: string }>
  >([]);

  readonly hayContenido = computed(() => this.eventos().length > 0);

  async ngOnInit(): Promise<void> {
    await this.refrescarEstado();
    await this.cargarProyectoYConfig();
    if (!this.activa()) await this.cargarUltimaSesionGuardada();
  }

  ngOnDestroy(): void {
    this.detenerPolling();
  }

  async iniciar(): Promise<void> {
    if (!this.url.trim()) {
      this.error.set('Indicá la URL de la aplicación.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.info.set('');
    try {
      const r = await this.api.mapeoIniciar(this.url, this.headless, {
        proyectoId: this.proyectoId() || undefined,
        loginAutomatico: this.esPlantillaSot() ? this.loginAutomatico : false
      });
      if (!r?.ok) throw new Error(r?.error || 'No se pudo iniciar');
      this.activa.set(!!r.activa);
      this.sesionId.set(String(r.sesionId || ''));
      this.urlActual.set(String(r.urlActual || r.urlInicio || ''));
      this.avisosLogin.set(Array.isArray(r.avisosLogin) ? r.avisosLogin : []);
      this.eventos.set([]);
      this.ultimoOrden.set(0);
      this.guardadoEn.set('');
      this.guardadoAbsoluto.set('');
      this.iniciarPolling();
    } catch (e: unknown) {
      this.error.set(e instanceof Error ? e.message : 'Error al iniciar mapeo');
    } finally {
      this.busy.set(false);
    }
  }

  async detener(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const r = await this.api.mapeoDetener();
      this.activa.set(false);
      this.detenerPolling();
      if (r?.guardadoEn) {
        this.guardadoEn.set(r.guardadoEn);
        this.guardadoAbsoluto.set(r.guardadoAbsoluto || '');
        this.info.set(r.mensaje || `Guardado en ${r.guardadoEn}`);
        if (r.gherkinPreview) this.gherkinPreview.set(r.gherkinPreview);
      } else {
        this.info.set(r?.mensaje || 'Grabación detenida.');
      }
      await this.actualizarPreviewCompleta();
      this.previewTab.set('resumen');
    } catch (e: unknown) {
      this.error.set(e instanceof Error ? e.message : 'Error al detener');
    } finally {
      this.busy.set(false);
    }
  }

  async liberar(): Promise<void> {
    if (this.activa()) {
      await this.detener();
      return;
    }
    this.busy.set(true);
    try {
      await this.api.mapeoLiberar();
      this.activa.set(false);
      this.sesionId.set('');
      this.urlActual.set('');
    } finally {
      this.busy.set(false);
    }
  }

  exportarJson(): void {
    if (!this.hayContenido()) {
      this.error.set('No hay clics grabados.');
      return;
    }
    window.open(this.api.mapeoExportarUrl(), '_blank');
  }

  async enviarAGenerar(): Promise<void> {
    if (!this.hayContenido()) {
      this.error.set('Grabá al menos un clic.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const r = await fetch(this.api.mapeoExportarUrl());
      if (!r.ok) throw new Error('No se pudo leer la sesión.');
      const json = (await r.json()) as { gherkin?: string };
      const gh = (json.gherkin || '').trim();
      if (!gh) throw new Error('Sin Gherkin en la sesión.');
      sessionStorage.setItem('runner.mapeoGherkin', gh);
      sessionStorage.setItem('runner.mapeoGherkinNombre', 'mapeo-ui-sesion.json');
      await this.router.navigate(['/generar']);
    } catch (e: unknown) {
      this.error.set(e instanceof Error ? e.message : 'No se pudo enviar a Generar');
    } finally {
      this.busy.set(false);
    }
  }

  async buscarManual(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const r = await this.api.mapeoBuscarCodigo(this.consultaManual, this.xpathManual);
      this.hitsManual.set(r?.coincidencias || []);
    } catch (e: unknown) {
      this.error.set(e instanceof Error ? e.message : 'Error en búsqueda');
    } finally {
      this.busy.set(false);
    }
  }

  setPreviewTab(tab: PreviewTab): void {
    this.previewTab.set(tab);
  }

  toggleEvento(orden?: number): void {
    if (!orden) return;
    this.eventoExpandido.update((v) => (v === orden ? null : orden));
  }

  copiar(texto?: string | null): void {
    if (!texto) return;
    void navigator.clipboard?.writeText(texto);
  }

  trackCoincidencia(
    index: number,
    h: { repoId?: string; archivo?: string; linea?: number }
  ): string {
    return `${index}|${h.repoId ?? ''}|${h.archivo ?? ''}|${h.linea ?? 0}`;
  }

  esPlantillaSot(): boolean {
    return this.plantilla() === 'sot';
  }

  private async cargarProyectoYConfig(): Promise<void> {
    try {
      const pr = await firstValueFrom(this.api.getRunnerIaProyecto());
      const activoId = pr?.proyectoActivoId || pr?.proyectoActivo?.id || '';
      this.proyectoId.set(activoId);
      this.proyectoNombre.set(pr?.proyectoActivo?.nombre || activoId);

      const p = pr?.proyectoActivo as { plantilla?: string } | undefined;
      const pl =
        p?.plantilla === 'sot' || (!p?.plantilla && activoId === 'sot-caja') ? 'sot' : 'generico';
      this.plantilla.set(pl);

      const r = await this.api.mapeoUrlDefault(activoId || undefined);
      if (r?.url) this.url = r.url;
      if (r?.proyectoNombre) this.proyectoNombre.set(String(r.proyectoNombre));
      this.loginAutomatico = r?.loginAutomaticoDefault ?? pl === 'sot';
      if (r?.usuario) this.usuarioConfig.set(String(r.usuario));
      if (r?.sucursalObjetivo) this.sucursalObjetivo.set(String(r.sucursalObjetivo));
      if (r?.error) this.error.set(String(r.error));
    } catch {
      /* ignore */
    }
  }

  private async cargarUltimaSesionGuardada(): Promise<void> {
    try {
      const r = await this.api.mapeoUltimaSesion();
      if (!r?.ok || r.disponible === false) return;
      if (r.guardadoEn) this.guardadoEn.set(r.guardadoEn);
      if (Array.isArray(r.eventos) && r.eventos.length) {
        this.eventos.set(r.eventos);
        this.ultimoOrden.set(Math.max(...r.eventos.map((e) => e.orden || 0)));
        this.info.set(`Última sesión: ${r.totalEventos ?? r.eventos.length} clic(s) · ${r.guardadoEn || 'mapeos-ui/'}`);
      }
      if (r.gherkin) this.gherkinPreview.set(r.gherkin.split('\n').slice(0, 20).join('\n'));
      await this.actualizarPreviewCompleta();
    } catch {
      /* ignore */
    }
  }

  private async actualizarPreviewCompleta(): Promise<void> {
    try {
      const r = await fetch(this.api.mapeoExportarUrl());
      if (!r.ok) return;
      const json = await r.json();
      this.jsonPreview.set(JSON.stringify(json, null, 2));
      const gh = (json as { gherkin?: string }).gherkin;
      if (gh) this.gherkinPreview.set(gh);
      const ev = (json as { eventos?: MapeoEventoUi[] }).eventos;
      if (Array.isArray(ev) && ev.length) this.eventos.set(ev);
    } catch {
      /* ignore */
    }
  }

  private async refrescarEventos(): Promise<void> {
    try {
      const r = await this.api.mapeoEventos(this.ultimoOrden());
      if (!r?.ok || !r.activa) return;
      const nuevos = r.eventos || [];
      if (nuevos.length) {
        this.eventos.update((prev) => [...prev, ...nuevos]);
        this.ultimoOrden.set(Math.max(...nuevos.map((e) => e.orden || 0), this.ultimoOrden()));
      }
    } catch {
      /* ignore */
    }
  }

  private async refrescarEstado(): Promise<void> {
    try {
      const r = await this.api.mapeoEstado();
      this.activa.set(!!r?.activa);
      if (r?.activa) {
        this.sesionId.set(String(r.sesionId || ''));
        this.urlActual.set(String(r.urlActual || ''));
        this.iniciarPolling();
        await this.refrescarEventos();
      }
    } catch {
      /* ignore */
    }
  }

  private iniciarPolling(): void {
    this.detenerPolling();
    this.pollTimer = setInterval(() => void this.refrescarEventos(), 1500);
  }

  private detenerPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
