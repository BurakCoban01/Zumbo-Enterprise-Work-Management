import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { finalize } from 'rxjs';
import { normalizeApiError, ZumboSessionService } from '@zumbo/modern-shared';

@Component({
  selector: 'zumbo-mobile-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar],
  templateUrl: './mobile-forgot-password.page.html',
  styleUrl: './mobile-auth-recovery.scss'
})
export class MobileForgotPasswordPage {
  private readonly forms = inject(FormBuilder);
  private readonly session = inject(ZumboSessionService);
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = this.forms.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });

  protected submit(): void {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true); this.error.set(null);
    this.session.forgotPassword(this.form.controls.email.value.trim()).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => this.sent.set(true),
      error: value => this.error.set(normalizeApiError(value).message || 'İstek tamamlanamadı. Lütfen yeniden deneyin.')
    });
  }
}
