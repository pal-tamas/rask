import { createFileRoute } from '@tanstack/react-router'

import AuthForm from '../components/AuthForm'

export const Route = createFileRoute('/register')({
  component: () => <AuthForm mode="register" />,
})
