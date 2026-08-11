import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { finalize } from 'rxjs';
import { normalizeApiError, ZumboSessionService } from '@zumbo/modern-shared';

@Component({
  selector: 'zumbo-mobile-reset-password',
  imports: [ReactiveFormsModule, RouterLink, IonButton, IonContent, IonHeader, IonInput, IonItem, IonTitle, IonToolbar],
  templateUrl: './mobile-reset-password.page.html',
  styleUrl: './mobile-auth-recovery.scss'
})
export class MobileResetPasswordPage {
  private readonly forms = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(ZumboSessionService);
  protected readonly token = this.route.snapshot.queryParamMap.get('token') || '';
  protected readonly busy = signal(false);
  protected readonly complete = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = this.forms.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(12)]],
    confirmation: ['', Validators.required]
  });

  protected submit(): void {
    const value = this.form.getRawValue();
    if (!this.token || this.form.invalid || value.newPassword !== value.confirmation || this.busy()) {
      if (value.newPassword !== value.confirmation) this.error.set('Parola alanları birbiriyle eşleşmiyor.');
      return;
    }
    this.busy.set(true); this.error.set(null);
    this.session.resetPassword(this.token, value.newPassword).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => this.complete.set(true),
      error: response => this.error.set(normalizeApiError(response).message || 'Bağlantı kullanılamadı veya süresi doldu.')
    });
  }
}
