import { Component } from '@angular/core';

import { AuthForm } from './auth-form';

@Component({
  selector: 'app-login',
  imports: [AuthForm],
  template: `<app-auth-form mode="login" />`,
})
export default class Login {}
