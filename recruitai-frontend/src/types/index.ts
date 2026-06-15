export type Role = 'HRAdmin' | 'TeamLead' | 'Viewer';

export interface User {
  email: string;
  role: Role;
  fullName?: string;
}

export interface SkillWeight {
  skill: string;
  weight: number; // 0.0 - 1.0
  category: string; // frontend, backend, data, etc.
}

export interface SkillGraph {
  requiredSkills: SkillWeight[];
  niceToHaveSkills: SkillWeight[];
  experienceYearsMin: number;
  seniority: string;
  domainKeywords: string[];
  jobEmbeddingText?: string;
  extractedAt: string;
}

export interface Job {
  id: string;
  title: string;
  description: string;
  department: string;
  isActive: boolean;
  createdAt: string;
  skillGraph?: SkillGraph | null;
}

export interface Candidate {
  id: string;
  fullName: string;
  email: string;
  createdAt: string;
}

export interface Application {
  id: string;
  jobId: string;
  candidateId: string;
  resumeS3Key: string;
  status: 'Queued' | 'Processing' | 'Scored' | 'Failed';
  fitScore?: number | null;
  rank?: number | null;
  errorMessage?: string | null;
  retryCount: number;
  candidate?: Candidate;
  job?: Job;
  createdAt: string;
}

export interface InterviewQuestion {
  category: string;
  question: string;
  difficulty: string; // junior, mid, senior
  rationale: string;
}

export interface InterviewKit {
  id: string;
  applicationId: string;
  questions: InterviewQuestion[];
  isGenerated: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface LeaderboardCandidateDto {
  rank: number;
  candidateId: string;
  name: string;
  fitScore: number;
  topSkillMatches: string[];
  status: string;
  applicationId: string;
}

export interface LeaderboardResult {
  jobId: string;
  jobTitle: string;
  totalApplicants: number;
  processedCount: number;
  candidates: LeaderboardCandidateDto[];
}

export interface InterviewKitQuestion {
  Category: string;
  Question: string;
  Difficulty: string;
  Rationale: string;
}

export interface InterviewKitResult {
  CandidateName: string;
  JobTitle: string;
  FitScore: number;
  Questions: InterviewKitQuestion[];
}

