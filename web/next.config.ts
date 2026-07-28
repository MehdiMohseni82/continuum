import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone", // lean container: only the server + traced deps are shipped
  webpack(config) {
    config.module.rules.push({
      test: /\.svg$/,
      use: ["@svgr/webpack"],
    });
    return config;
  },
  images: {
    localPatterns:[
      {
        pathname: '/**',
      },
    ]
  },
  turbopack: {
    root: __dirname,
    rules: {
      "*.svg": {
        loaders: ["@svgr/webpack"],
        as: "*.js",
      },
    },
  },
};

export default nextConfig;
