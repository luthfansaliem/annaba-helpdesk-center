export const stats = [
  { label: "Total Employees", value: "1,284", delta: "12.5%", up: true, color: "#0b62e6", bg: "#e5efff", icon: "users", spark: "M0 40 L12 30 L22 36 L34 24 L46 32 L58 14 L70 22 L84 6" },
  { label: "Active Projects", value: "48", delta: "8.3%", up: true, color: "#f4694f", bg: "#ffe9e4", icon: "briefcase", spark: "M0 38 L14 28 L26 34 L38 20 L50 30 L62 16 L74 24 L84 8" },
  { label: "Pending Approvals", value: "23", delta: "15.6%", up: false, color: "#14b8a6", bg: "#d9f5ef", icon: "clock", spark: "M0 10 L12 22 L24 16 L36 30 L48 22 L60 34 L72 26 L84 38" },
  { label: "Completion Rate", value: "94.8%", delta: "6.2%", up: true, color: "#0b62e6", bg: "#e5efff", icon: "check", spark: "M0 36 L12 40 L22 26 L34 34 L46 18 L58 30 L70 14 L84 22" },
];

export type Range = "7D" | "30D" | "3M" | "1Y";
export const ranges: Range[] = ["7D", "30D", "3M", "1Y"];

const base = [
  { name: "Engineering", color: "#0b62e6", data: [45, 52, 62, 48, 58, 70, 62, 72, 66, 78, 88, 96] },
  { name: "Marketing", color: "#f4694f", data: [38, 42, 50, 44, 52, 60, 54, 62, 58, 68, 78, 84] },
  { name: "Sales", color: "#14b8a6", data: [20, 28, 34, 30, 36, 48, 40, 46, 42, 52, 62, 68] },
];

const clamp = (n: number) => Math.max(5, Math.min(98, Math.round(n)));
/** Deterministic variation per range so the switcher visibly changes the chart. */
const shape = (data: number[], k: number) => data.map((v, i) => clamp(v * (0.8 + 0.25 * Math.sin((i + k) * 0.9)) + k * 2));

export const performanceByRange: Record<Range, { labels: string[]; series: typeof base }> = {
  "7D": { labels: ["Apr 15", "Apr 17", "Apr 19", "Apr 21", "Apr 22"], series: base.map((s, i) => ({ ...s, data: shape(s.data, i + 1) })) },
  "30D": { labels: ["Mar 24", "Mar 31", "Apr 7", "Apr 14", "Apr 21"], series: base },
  "3M": { labels: ["Jan", "Feb", "Mar", "Apr", "May"], series: base.map((s, i) => ({ ...s, data: shape(s.data, i + 4) })) },
  "1Y": { labels: ["May", "Aug", "Nov", "Feb", "Apr"], series: base.map((s, i) => ({ ...s, data: shape(s.data, i + 8) })) },
};

export const projectStatus = [
  { label: "On Track", value: 28, color: "#0b62e6" },
  { label: "At Risk", value: 10, color: "#f4694f" },
  { label: "Delayed", value: 6, color: "#fbbf24" },
  { label: "Completed", value: 4, color: "#14b8a6" },
];

export const activities = [
  { title: "Project Aurora completed", meta: "Marketing • 2 hours ago", kind: "done" },
  { title: "New employee joined", meta: "Dewi Lestari • 4 hours ago", kind: "user" },
  { title: "Approval request", meta: "Budget Q2 2025 • 5 hours ago", kind: "file" },
  { title: "Task updated", meta: "Website Redesign • 6 hours ago", kind: "edit" },
];

export const performers = [
  { rank: 1, name: "Andi Pratama", dept: "Engineering", score: 98, color: "#14b8a6" },
  { rank: 2, name: "Siti Rahma", dept: "Product", score: 96, color: "#14b8a6" },
  { rank: 3, name: "Budi Santoso", dept: "Design", score: 94, color: "#0b62e6" },
  { rank: 4, name: "Rina Aprilia", dept: "Marketing", score: 92, color: "#0b62e6" },
  { rank: 5, name: "Fajar Nugroho", dept: "Sales", score: 90, color: "#0b62e6" },
];

export const recentProjects = [
  { name: "Website Redesign", dept: "Marketing", progress: 60, due: "Due in 12 days", color: "#0b62e6" },
  { name: "Mobile App Development", dept: "Engineering", progress: 45, due: "Due in 18 days", color: "#f4694f" },
  { name: "CRM Integration", dept: "IT", progress: 80, due: "Due in 5 days", color: "#14b8a6" },
  { name: "Brand Campaign Q2", dept: "Marketing", progress: 25, due: "Due in 21 days", color: "#f59e0b" },
];

export type Employee = {
  id: string; name: string; dept: string; position: string;
  employment: "Active" | "On Leave" | "Inactive"; account: "Active" | "Disabled";
  email: string; phone: string; joined: string;
};

export const employees: Employee[] = [
  { id: "EMP-1001", name: "Andi Pratama", dept: "Engineering", position: "Senior Developer", employment: "Active", account: "Active", email: "andi.pratama@company.com", phone: "+62 812-3456-7890", joined: "12 Jan 2024" },
  { id: "EMP-1002", name: "Siti Rahma", dept: "Product", position: "Product Manager", employment: "Active", account: "Active", email: "siti.rahma@company.com", phone: "+62 813-2211-4455", joined: "03 Mar 2023" },
  { id: "EMP-1003", name: "Budi Santoso", dept: "Design", position: "UI/UX Designer", employment: "On Leave", account: "Active", email: "budi.santoso@company.com", phone: "+62 811-9988-1122", joined: "21 Aug 2022" },
  { id: "EMP-1004", name: "Rina Aprilia", dept: "Marketing", position: "Marketing Specialist", employment: "Active", account: "Active", email: "rina.aprilia@company.com", phone: "+62 815-7766-3344", joined: "09 Nov 2023" },
  { id: "EMP-1005", name: "Fajar Nugroho", dept: "Sales", position: "Account Executive", employment: "Inactive", account: "Disabled", email: "fajar.nugroho@company.com", phone: "+62 817-5544-8899", joined: "17 Jun 2021" },
];

export const departments = ["Engineering", "Product", "Design", "Marketing", "Sales"];
export const positions = ["Senior Developer", "Product Manager", "UI/UX Designer", "Marketing Specialist", "Account Executive"];
