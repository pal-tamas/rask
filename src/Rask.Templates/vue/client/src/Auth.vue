<script setup lang="ts">
import { computed, ref } from 'vue'
import { login, register, type AuthFailure } from './rask/browser/auth'

/*
  Sign in and registration, over the endpoints Rask.Auth maps at /api/auth. The generated
  client is typed, so there is no status code to read and no shape to guess, and the cookie
  it sets is HttpOnly — this page never sees a token.
*/
const props = defineProps<{ mode: 'login' | 'register' }>()

const registering = computed(() => props.mode === 'register')
const email = ref('')
const password = ref('')
const failure = ref<AuthFailure | null>(null)
const busy = ref(false)

async function submit() {
  busy.value = true
  failure.value = null

  const credentials = { email: email.value, password: password.value }
  const result = registering.value
    ? await register(credentials)
    : await login(credentials)

  busy.value = false
  if (result.ok) window.location.assign('/')
  else failure.value = result.failure
}
</script>

<template>
  <main class="hero min-h-screen bg-base-200">
    <div class="hero-content w-full max-w-sm flex-col">
      <h1 class="text-2xl font-bold">
        {{ registering ? 'Create an account' : 'Sign in' }}
      </h1>

      <form class="card bg-base-100 w-full shadow-sm" @submit.prevent="submit">
        <div class="card-body gap-4">
          <div v-if="failure" role="alert" class="alert alert-error">
            <span>{{ failure.message ?? failure.error }}</span>
          </div>

          <label class="fieldset">
            <span class="fieldset-legend">Email</span>
            <input
              class="input w-full"
              type="email"
              autocomplete="username"
              required
              v-model="email"
            />
          </label>

          <label class="fieldset">
            <span class="fieldset-legend">Password</span>
            <input
              class="input w-full"
              type="password"
              :autocomplete="registering ? 'new-password' : 'current-password'"
              required
              v-model="password"
            />
          </label>

          <div class="card-actions">
            <button class="btn btn-primary btn-block" type="submit" :disabled="busy">
              {{ registering ? 'Create account' : 'Sign in' }}
            </button>
          </div>

          <p class="text-sm">
            <a class="link link-primary" :href="registering ? '/login' : '/register'">
              {{ registering ? 'Already have an account?' : 'No account yet?' }}
            </a>
          </p>
        </div>
      </form>
    </div>
  </main>
</template>
