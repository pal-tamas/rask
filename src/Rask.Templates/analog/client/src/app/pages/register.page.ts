import { Component } from '@angular/core'

import { AuthForm } from './auth-form'

@Component({
  selector: 'app-register',
  imports: [AuthForm],
  template: `<app-auth-form mode="register" />`,
})
export default class Register {}
