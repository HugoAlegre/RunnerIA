/** Proyecto RunnerIA (Catalogo/proyectos-runner.json). */
export type ProyectoPlantilla = 'sot' | 'generico';

export interface RunnerProyecto {
  id: string;
  nombre: string;
  iaNombre?: string;
  descripcion?: string;
  catalogoConfigRelativo?: string;
  /** sot = presets Accusys; generico = URL/SQL/Jira editables por proyecto. */
  plantilla?: ProyectoPlantilla;
  /** Conexiones y herramientas del proyecto (genérico). */
  integraciones?: import('./proyecto-integracion').ProyectoIntegracion[];
}
