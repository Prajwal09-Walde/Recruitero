import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { CandidateStatusRow, CandidateProgress } from './CandidateStatusRow';

const mockPush = jest.fn();
jest.mock('next/navigation', () => ({
  useRouter() {
    return {
      push: mockPush,
    };
  },
}));

describe('CandidateStatusRow', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders candidate name correctly', () => {
    const candidate: CandidateProgress = {
      applicationId: 'app-1',
      name: 'John Doe',
      status: 'Queued',
    };

    render(<CandidateStatusRow candidate={candidate} />);
    expect(screen.getByText('John Doe')).toBeInTheDocument();
    expect(screen.getByText('Queued')).toBeInTheDocument();
  });

  it('renders processing state with spinner', () => {
    const candidate: CandidateProgress = {
      applicationId: 'app-2',
      name: 'Alice Smith',
      status: 'Processing',
    };

    render(<CandidateStatusRow candidate={candidate} />);
    expect(screen.getByText('Alice Smith')).toBeInTheDocument();
    expect(screen.getByText('Processing')).toBeInTheDocument();
  });

  it('renders fit score when candidate is Scored', () => {
    const candidate: CandidateProgress = {
      applicationId: 'app-3',
      name: 'Bob Johnson',
      status: 'Scored',
      fitScore: 85.4,
    };

    render(<CandidateStatusRow candidate={candidate} />);
    expect(screen.getByText('Bob Johnson')).toBeInTheDocument();
    expect(screen.getByText('Scored')).toBeInTheDocument();
    expect(screen.getByText('85% Fit')).toBeInTheDocument();
  });

  it('renders error message when candidate processing has Failed', () => {
    const candidate: CandidateProgress = {
      applicationId: 'app-4',
      name: 'Error Candidate',
      status: 'Failed',
      errorMessage: 'Resume format not supported',
    };

    render(<CandidateStatusRow candidate={candidate} />);
    expect(screen.getByText('Error Candidate')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('Resume format not supported')).toBeInTheDocument();
  });

  it('shows View Kit button and navigates when clicked if kitReady is true', () => {
    const candidate: CandidateProgress = {
      applicationId: 'app-5',
      name: 'Charlie Brown',
      status: 'Scored',
      fitScore: 92,
      kitReady: true,
    };

    render(<CandidateStatusRow candidate={candidate} />);
    const button = screen.getByRole('button', { name: /view kit/i });
    expect(button).toBeInTheDocument();

    fireEvent.click(button);
    expect(mockPush).toHaveBeenCalledWith('/applications/app-5/kit');
  });
});
