<script lang="ts">
  import Greeting from './lib/Greeting.svelte'
  import Auth from './lib/Auth.svelte'

  // No router, deliberately — see the note in the React template. The path is read once,
  // and deep links work because the dev server and the host both fall back to index.html.
  const path = window.location.pathname
</script>

{#if path === '/login'}
  <Auth mode="login" />
{:else if path === '/register'}
  <Auth mode="register" />
{:else}
  <Greeting />
{/if}
