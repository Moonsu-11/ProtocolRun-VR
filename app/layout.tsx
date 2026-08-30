import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "ProtocolRun-VR",
  description: "Evidence-based VR study operations, bounded recovery and verification.",
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ko">
      <body className="antialiased">{children}</body>
    </html>
  );
}
