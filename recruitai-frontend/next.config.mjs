import bundleAnalyzer from '@next/bundle-analyzer';

const withBundleAnalyzer = bundleAnalyzer({
  enabled: process.env.ANALYZE === 'true',
});

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  eslint: {
    // strict: ESLint errors will fail the production build
    ignoreDuringBuilds: false,
  },
  typescript: {
    // strict: TypeScript compiler errors will fail the production build
    ignoreBuildErrors: false,
  },
  images: {
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'recruitai-resumes.s3.amazonaws.com',
      },
      {
        protocol: 'https',
        hostname: 'recruitai-resumes.s3.us-east-1.amazonaws.com',
      },
    ],
  },
  async rewrites() {
    const backendUrl = process.env.NEXT_PUBLIC_API_URL || 'https://recruitai.io';
    return [
      {
        source: '/api/:path*',
        destination: `${backendUrl}/api/:path*`,
      },
      {
        source: '/hubs/:path*',
        destination: `${backendUrl}/hubs/:path*`,
      },
    ];
  },
};

export default withBundleAnalyzer(nextConfig);
