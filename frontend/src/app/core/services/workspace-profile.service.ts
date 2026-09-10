import { Injectable, computed, signal } from '@angular/core';

/** Perfil de uso del Runner: presets Accusys SOT o instancia vacía/configurable. */
export type WorkspaceProfileMode = 'sot-accusys' | 'generico';

export interface AmbientePreset {
  url: string;
  sqlServidor: string;
  authOpenIdUrl: string;
}

const LS_KEY = 'sot-runner-workspace-profile';
const LS_PROYECTO = 'sot-runner-proyecto-activo';

/** ID del proyecto SOT Accusys por defecto (presets DEV/QA). */
export const PROYECTO_SOT_CAJA_ID = 'sot-caja';

/** Presets solo para el perfil SOT Accusys (DEV/QA). */
export const PRESETS_SOT_ACCUSYS = {
  Dev: {
    url: 'http://sot-app.accusys-dev.io/home/welcome',
    sqlServidor: 'sqlsot.accusys-dev.io',
    authOpenIdUrl: ''
  },
  QA: {
    url: 'https://sot-app.accusys-qa.io/home/welcome',
    sqlServidor: 'sqlsot.accusys-qa.io',
    authOpenIdUrl:
      'https://auth.accusys-qa.io/realms/uniweb-cloud/.well-known/openid-configuration'
  },
  Custom: { url: '', sqlServidor: '', authOpenIdUrl: '' }
} as const satisfies Record<string, AmbientePreset>;

/** Vacío: el operador carga URL, SQL y COBIS de su instancia. */
export const PRESETS_VACIO: AmbientePreset = {
  url: '',
  sqlServidor: '',
  authOpenIdUrl: ''
};

@Injectable({ providedIn: 'root' })
export class WorkspaceProfileService {
  readonly mode = signal<WorkspaceProfileMode | null>(this.leer());
  readonly proyectoId = signal<string | null>(this.leerProyectoId());
  readonly elegido = computed(() => this.proyectoId() !== null);
  readonly esSotAccusys = computed(() => this.mode() === 'sot-accusys');
  readonly esGenerico = computed(() => this.mode() === 'generico');

  private leer(): WorkspaceProfileMode | null {
    try {
      const v = localStorage.getItem(LS_KEY);
      if (v === 'sot-accusys' || v === 'generico') return v;
    } catch {
      /* ignore */
    }
    return null;
  }

  private leerProyectoId(): string | null {
    try {
      const v = localStorage.getItem(LS_PROYECTO);
      return v?.trim() || null;
    } catch {
      return null;
    }
  }

  elegir(mode: WorkspaceProfileMode): void {
    this.mode.set(mode);
    try {
      localStorage.setItem(LS_KEY, mode);
    } catch {
      /* ignore */
    }
  }

  /** Asocia proyecto RunnerIA y perfil de presets (SOT Accusys vs genérico). */
  elegirProyecto(proyectoId: string, plantilla?: 'sot' | 'generico' | null): void {
    const id = proyectoId.trim();
    this.proyectoId.set(id);
    try {
      localStorage.setItem(LS_PROYECTO, id);
    } catch {
      /* ignore */
    }
    const esSot =
      plantilla === 'sot' ||
      (plantilla !== 'generico' && id.toLowerCase() === PROYECTO_SOT_CAJA_ID);
    const mode: WorkspaceProfileMode = esSot ? 'sot-accusys' : 'generico';
    this.elegir(mode);
  }

  /** Cambia perfil desde Configuración (mantiene casos; solo cambia presets/UX). */
  cambiar(mode: WorkspaceProfileMode): void {
    this.elegir(mode);
  }

  presetAmbiente(ambiente: 'Dev' | 'QA' | 'Custom'): AmbientePreset {
    if (this.mode() !== 'sot-accusys') return PRESETS_VACIO;
    return PRESETS_SOT_ACCUSYS[ambiente] || PRESETS_VACIO;
  }

  etiquetaProducto(): string {
    return this.esSotAccusys() ? 'SOT' : 'aplicación';
  }

  etiquetaSql(): string {
    return this.esSotAccusys() ? 'SQL SOT' : 'SQL de la aplicación';
  }

  etiquetaCobis(): string {
    return this.esSotAccusys() ? 'COBIS' : 'Base operativa / host';
  }

  /** Subtítulo en la barra superior del Runner (tras elegir perfil). */
  etiquetaRunnerBar(): string {
    return this.proyectoId() ? this.proyectoId()! : 'Automatización';
  }
}
