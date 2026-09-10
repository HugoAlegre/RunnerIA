import { Component, EventEmitter, HostListener, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

/** Diálogo sot-dialog reutilizable (confirmación o aviso). */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './confirm-dialog.component.html',
  styleUrl: './confirm-dialog.component.scss'
})
export class ConfirmDialogComponent {
  /** Si false, no renderiza. */
  @Input() open = false;
  @Input() title = '';
  @Input() body = '';
  /** confirm = Cancelar + Confirmar; alert = solo Entendido */
  @Input() mode: 'confirm' | 'alert' = 'confirm';
  @Input() confirmLabel = 'Confirmar';
  @Input() cancelLabel = 'Cancelar';
  @Input() alertLabel = 'Entendido';
  @Input() danger = false;
  /** Estilo OK en modo alert (conexión exitosa). */
  @Input() ok = false;
  @Input() eyebrow = '';

  @Output() closed = new EventEmitter<boolean>();

  get resolvedEyebrow(): string {
    if (this.eyebrow) return this.eyebrow;
    if (this.mode === 'alert') return this.ok ? 'Conexión' : 'Error de conexión';
    return this.danger ? 'Confirmación' : 'Aviso';
  }

  @HostListener('document:keydown.escape')
  onEsc(): void {
    if (this.open) this.closed.emit(false);
  }

  onBackdrop(): void {
    this.closed.emit(false);
  }

  confirm(): void {
    this.closed.emit(true);
  }

  cancel(): void {
    this.closed.emit(false);
  }
}
