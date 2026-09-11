<template>
  <div>
    <NuxtRouteAnnouncer />
    <!-- rask:ifnot data -->
    <NuxtWelcome />
    <!-- rask:end -->
    <!-- rask:if cqrs data -->
    <NuxtPage />
    <!-- rask:end -->
  </div>
</template>
