import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface PageTocItem {
  id: string;
  label: string;
  /** 1 = principal, 2 = indentado (manual) */
  level?: 1 | 2;
  /** Texto de separador mostrado antes de este ítem */
  sepBefore?: string;
}

/** Índice lateral de secciones (Config / Ayuda). */
@Component({
  selector: 'app-page-toc',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './page-toc.component.html',
  styleUrl: './page-toc.component.scss'
})
export class PageTocComponent {
  @Input() title = 'Secciones';
  @Input() items: PageTocItem[] = [];
  @Output() navigate = new EventEmitter<string>();

  onClick(id: string, ev: Event): void {
    ev.preventDefault();
    this.navigate.emit(id);
  }
}
