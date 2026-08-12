import {Injectable,inject} from '@angular/core';
import {ZumboApiClient} from '@zumbo/modern-shared';
import {PublicIntakeConfirmation,PublicIntakeForm} from './mobile-public-intake.models';
@Injectable() export class MobilePublicIntakeService{private readonly api=inject(ZumboApiClient);load(publicId:string){return this.api.get<PublicIntakeForm>(`/api/intake/public/forms/${encodeURIComponent(publicId)}`);}submit(publicId:string,body:FormData){return this.api.post<PublicIntakeConfirmation>(`/api/intake/public/forms/${encodeURIComponent(publicId)}/submissions`,body,{idempotencyKey:this.api.newIdempotencyKey()});}}
