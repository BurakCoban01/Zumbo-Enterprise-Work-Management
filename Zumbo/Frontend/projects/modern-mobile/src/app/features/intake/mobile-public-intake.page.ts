import {Component,inject,signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {ActivatedRoute} from '@angular/router';
import {IonContent,IonHeader,IonTitle,IonToolbar} from '@ionic/angular/standalone';
import {finalize} from 'rxjs';
import {normalizeApiError} from '@zumbo/modern-shared';
import {MobileConnectivityService} from '../../shell/mobile-connectivity.service';
import {newPublicIntakeModel,publicIntakeFormData,validatePublicIntake} from './mobile-public-intake.core';
import {PublicIntakeConfirmation,PublicIntakeField,PublicIntakeForm,PublicIntakeModel} from './mobile-public-intake.models';
import {MobilePublicIntakeService} from './mobile-public-intake.service';
@Component({selector:'zumbo-mobile-public-intake',imports:[FormsModule,IonContent,IonHeader,IonTitle,IonToolbar],providers:[MobilePublicIntakeService],templateUrl:'./mobile-public-intake.page.html',styleUrl:'./mobile-public-intake.page.scss'})
export class MobilePublicIntakePage{private readonly route=inject(ActivatedRoute);private readonly api=inject(MobilePublicIntakeService);protected readonly connectivity=inject(MobileConnectivityService);private readonly publicId=this.route.snapshot.paramMap.get('publicId')||'';protected readonly form=signal<PublicIntakeForm|null>(null);protected readonly loading=signal(true);protected readonly busy=signal(false);protected readonly error=signal<string|null>(null);protected readonly confirmation=signal<PublicIntakeConfirmation|null>(null);protected model:PublicIntakeModel|null=null;
 constructor(){this.load();}
 protected load(){if(!this.publicId){this.loading.set(false);this.error.set('Talep bağlantısı geçersiz.');return;}this.loading.set(true);this.error.set(null);this.api.load(this.publicId).pipe(finalize(()=>this.loading.set(false))).subscribe({next:value=>{this.form.set(value);this.model=newPublicIntakeModel(value);},error:value=>this.error.set(this.message(value,'Paylaşılan form yüklenemedi.'))});}
 protected capture(field:PublicIntakeField,event:Event){const files=Array.from((event.target as HTMLInputElement).files??[]);if(this.model)this.model.files[field.key]=files;}
 protected submit(){const form=this.form(),model=this.model,validation=validatePublicIntake(form,model);if(validation){this.error.set(validation);return;}if(!form||!model||this.busy()||this.connectivity.offline())return;this.busy.set(true);this.error.set(null);this.api.submit(this.publicId,publicIntakeFormData(form,model)).pipe(finalize(()=>this.busy.set(false))).subscribe({next:value=>{this.confirmation.set(value);this.model=newPublicIntakeModel(form);},error:value=>this.error.set(this.message(value,'Talep gönderilemedi.'))});}
 private message(value:unknown,fallback:string){const error=normalizeApiError(value);return({INTAKE_FORM_NOT_FOUND:'Paylaşılan form bulunamadı.',INTAKE_FORM_ARCHIVED:'Bu talep formu artık kullanılamıyor.',VALIDATION_ERROR:'Form alanlarını kontrol edip yeniden deneyin.',RATE_LIMITED:'Çok sayıda deneme yapıldı; kısa süre sonra yeniden deneyin.'} as Record<string,string>)[error.code]||fallback;}
}
