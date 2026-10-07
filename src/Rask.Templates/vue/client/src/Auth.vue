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

const input =
  'w-full rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-zinc-400 dark:border-zinc-700 dark:bg-zinc-950'

async function submit() {
  busy.value = true
  failure.value = null

  const credentials = { email: email.value, password: password.value }
  const result = registering.value ? await register(credentials) : await login(credentials)

  busy.value = false
  if (result.ok) window.location.assign('/')
  else failure.value = result.failure
}
</script>

<template>
  <main class="grid min-h-screen place-items-center px-4">
    <div class="flex w-full max-w-sm flex-col items-center gap-6">
      <h1 class="text-2xl font-bold">
        {{ registering ? 'Create an account' : 'Sign in' }}
      </h1>

      <form
        class="flex w-full flex-col gap-4 rounded-xl border border-zinc-200 bg-white p-6 shadow-xs dark:border-zinc-800 dark:bg-zinc-900"
        @submit.prevent="submit"
      >
        <div
          v-if="failure"
          role="alert"
          class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
        >
          {{ failure.message ?? failure.error }}
        </div>

        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Email</span>
          <input :class="input" type="email" autocomplete="username" required v-model="email" />
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Password</span>
          <input
            :class="input"
            type="password"
            :autocomplete="registering ? 'new-password' : 'current-password'"
            required
            v-model="password"
          />
        </label>

        <button
          class="w-full rounded-lg bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
          type="submit"
          :disabled="busy"
        >
          {{ registering ? 'Create account' : 'Sign in' }}
        </button>

        <p class="text-sm">
          <a
            class="underline-offset-4 hover:underline"
            :href="registering ? '/login' : '/register'"
          >
            {{ registering ? 'Already have an account?' : 'No account yet?' }}
          </a>
        </p>
      </form>
    </div>
  </main>
</template>
