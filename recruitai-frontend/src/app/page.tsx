import { redirect } from 'next/navigation';

/**
 * Root route — immediately redirect to the login page.
 * AuthGuard will handle redirecting authenticated users to the jobs dashboard.
 */
export default function RootPage() {
  redirect('/login');
}
