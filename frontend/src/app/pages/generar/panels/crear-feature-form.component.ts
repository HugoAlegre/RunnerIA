import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  acceptAtributoInput,
  badgeTipoArchivo,
  MAX_ARCHIVOS
} from '../upload-archivos.util';

export interface DestinoModuloOpt {
  id: string;
  nombre: string;
  fijo?: boolean;
}

@Component({
  selector: 'app-crear-feature-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './crear-feature-form.component.html',
  styleUrl: './crear-feature-form.component.scss'
})
export class CrearFeatureFormComponent {
  @Input() jiraUrl = '';
  @Input() nombreFeature = '';
  @Input() detalle = '';
  @Input() texto = '';
  @Input() grupoId = '__auto__';
  @Input() grupoNuevo = '';
  @Input() moduloSugerido = '';
  @Input() destinos: DestinoModuloOpt[] = [];
  @Input() destinoLabel = '';
  @Input() docs: File[] = [];
  @Input() analizando = false;
  @Input() faseAnalizar: 'idle' | 'jira' | 'mapa' | 'gherkin' | 'listo' = 'idle';
  @Input() status = '';
  @Input() statusTipo = 'info';
  @Input() tienePreview = false;

  @Output() jiraUrlChange = new EventEmitter<string>();
  @Output() nombreFeatureChange = new EventEmitter<string>();
  @Output() detalleChange = new EventEmitter<string>();
  @Output() textoChange = new EventEmitter<string>();
  @Output() grupoIdChange = new EventEmitter<string>();
  @Output() grupoNuevoChange = new EventEmitter<string>();
  @Output() archivosRecibidos = new EventEmitter<File[]>();
  @Output() quitarDoc = new EventEmitter<number>();
  @Output() limpiarDocs = new EventEmitter<void>();
  @Output() analizar = new EventEmitter<void>();
  @Output() limpiar = new EventEmitter<void>();

  readonly acceptArchivos = acceptAtributoInput();
  readonly maxArchivos = MAX_ARCHIVOS;
  dragOver = false;

  get detalleChars(): number {
    return this.detalle.trim().length;
  }

  badgeDoc(nombre: string): string {
    return badgeTipoArchivo(nombre);
  }

  onDragOver(ev: DragEvent): void {
    ev.preventDefault();
    ev.stopPropagation();
    this.dragOver = true;
  }

  onDragLeave(ev: DragEvent): void {
    ev.preventDefault();
    ev.stopPropagation();
    this.dragOver = false;
  }

  onDrop(ev: DragEvent): void {
    ev.preventDefault();
    ev.stopPropagation();
    this.dragOver = false;
    const list = ev.dataTransfer?.files ? Array.from(ev.dataTransfer.files) : [];
    if (list.length) this.archivosRecibidos.emit(list);
  }

  onFileInputChange(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const list = input.files ? Array.from(input.files) : [];
    if (list.length) this.archivosRecibidos.emit(list);
    input.value = '';
  }
}
