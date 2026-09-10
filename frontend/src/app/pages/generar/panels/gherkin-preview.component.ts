import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-gherkin-preview',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gherkin-preview.component.html',
  styleUrl: './gherkin-preview.component.scss'
})
export class GherkinPreviewComponent {
  @Input() featurePreview = '';
  @Input() editandoArchivo = '';
  /** Archivo(s) .feature cargados por drag-drop (solo informativo). */
  @Input() origenArchivo = '';
  @Input() mostrarImportarLote = false;
  @Input() forzarIncompleto = false;
  @Input() sobrescribirLote = false;
  @Input() tienePreview = false;
  @Input() detalle = '';

  @Output() featurePreviewChange = new EventEmitter<string>();
  @Output() forzarIncompletoChange = new EventEmitter<boolean>();
  @Output() sobrescribirLoteChange = new EventEmitter<boolean>();
  @Output() guardar = new EventEmitter<void>();
  @Output() verModulos = new EventEmitter<void>();
  @Output() importarLote = new EventEmitter<void>();
}
