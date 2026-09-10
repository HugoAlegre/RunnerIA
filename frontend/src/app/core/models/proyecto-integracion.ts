/** Integración configurable por proyecto (URL app, SQL, Jira, Xray, etc.). */
export type ProyectoIntegracionTipo =
  | 'app'
  | 'sql'
  | 'jira'
  | 'xray'
  | 'azure-devops'
  | 'testrail'
  | 'otro';

export interface ProyectoIntegracion {
  id: string;
  tipo: ProyectoIntegracionTipo;
  etiqueta?: string;
  url?: string;
  notas?: string;
}

export const INTEGRACION_TIPOS: { id: ProyectoIntegracionTipo; label: string }[] = [
  { id: 'app', label: 'URL aplicación' },
  { id: 'sql', label: 'Base de datos' },
  { id: 'jira', label: 'Jira' },
  { id: 'xray', label: 'Xray' },
  { id: 'azure-devops', label: 'Azure DevOps' },
  { id: 'testrail', label: 'TestRail' },
  { id: 'otro', label: 'Otro' }
];

export function integracionesGenericasPorDefecto(): ProyectoIntegracion[] {
  return [
    { id: 'app', tipo: 'app', etiqueta: 'URL de la aplicación', url: '' },
    { id: 'sql', tipo: 'sql', etiqueta: 'Conexión base de datos', url: '' },
    { id: 'jira', tipo: 'jira', etiqueta: 'Jira (opcional)', url: '' }
  ];
}
