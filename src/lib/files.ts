import { api } from "@/lib/api/client";
import type { UploadedFile } from "@/lib/api/types";

/** What the server accepts (it checks the content too). Keeps the picker honest; the server stays the authority. */
export const FILE_ACCEPT = ".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv";
export const MAX_FILE_BYTES = 8 * 1024 * 1024;

export function fmtSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** Sends one file as multipart form data. The browser sets the boundary, so no content type is given. */
export function uploadFile<T = UploadedFile>(path: string, file: File): Promise<T> {
  const fd = new FormData(); fd.append("file", file, file.name);
  return api<T>(path, { method: "POST", rawBody: fd });
}
