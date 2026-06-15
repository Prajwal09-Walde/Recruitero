'use client';

import React from 'react';
import { DndContext, useDraggable, useDroppable, DragEndEvent } from '@dnd-kit/core';
import { LeaderboardCandidateDto } from '@/types';
import { cn } from '@/lib/utils';
import { ArrowRightLeft } from 'lucide-react';

interface KanbanBoardProps {
  candidates: LeaderboardCandidateDto[];
  onStatusChange: (applicationId: string, nextStatus: string) => void;
  userRole?: string;
}

const COLUMNS = [
  { id: 'Queued', title: 'Queued', color: 'border-slate-500/20 bg-slate-500/5 text-slate-400' },
  { id: 'Processing', title: 'Processing', color: 'border-violet-500/20 bg-violet-500/5 text-violet-400' },
  { id: 'Scored', title: 'Scored', color: 'border-blue-500/20 bg-blue-500/5 text-blue-400' },
  { id: 'SentToRecruiter', title: 'Sent to Team Lead', color: 'border-cyan-500/20 bg-cyan-500/5 text-cyan-400' },
  { id: 'Shortlisted', title: 'Shortlisted', color: 'border-emerald-500/20 bg-emerald-500/5 text-emerald-400' },
  { id: 'Rejected', title: 'Rejected', color: 'border-rose-500/20 bg-rose-500/5 text-rose-400' },
];

export function KanbanBoard({ candidates, onStatusChange, userRole }: KanbanBoardProps) {
  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over) return;

    const applicationId = active.id as string;
    const targetStatus = over.id as string;
    const candidate = candidates.find((c) => c.applicationId === applicationId);

    if (!candidate) return;

    const isTeamLeadRole = userRole === 'TeamLead';
    
    if (isTeamLeadRole) {
      const allowedTeamLeadStatuses = ['SentToRecruiter', 'Shortlisted', 'Rejected'];
      if (
        allowedTeamLeadStatuses.includes(candidate.status) &&
        allowedTeamLeadStatuses.includes(targetStatus) &&
        candidate.status !== targetStatus
      ) {
        onStatusChange(applicationId, targetStatus);
      }
    } else {
      const allowedAdminSource = ['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'];
      const allowedAdminTarget = ['SentToRecruiter', 'Shortlisted', 'Rejected'];
      if (
        allowedAdminSource.includes(candidate.status) &&
        allowedAdminTarget.includes(targetStatus) &&
        candidate.status !== targetStatus
      ) {
        onStatusChange(applicationId, targetStatus);
      }
    }
  };

  const columns = COLUMNS.filter(col => {
    if (userRole === 'TeamLead') {
      return ['SentToRecruiter', 'Shortlisted', 'Rejected'].includes(col.id);
    }
    return true; // HRAdmin sees all columns
  });

  return (
    <DndContext onDragEnd={handleDragEnd}>
      <div className="flex flex-col gap-4 w-full glass-panel p-6 rounded-2xl border border-white/5">
        <div>
          <h3 className="font-bold text-lg flex items-center gap-2">
            Candidate Pipeline
          </h3>
          <p className="text-xs text-muted-foreground mt-0.5">
            {userRole === 'TeamLead' 
              ? 'Drag Sent to Team Lead candidates to Shortlisted or Rejected columns.'
              : 'Drag Scored candidates to Sent to Team Lead, Shortlisted, or Rejected columns.'}
          </p>
        </div>

        <div className="flex gap-4 mt-2 select-none overflow-x-auto pb-3 custom-scrollbar">
          {columns.map((col) => {
            const colCandidates = candidates.filter((c) => c.status === col.id);
            return (
              <KanbanColumn
                key={col.id}
                id={col.id}
                title={col.title}
                candidates={colCandidates}
                colorClass={col.color}
              />
            );
          })}
        </div>
      </div>
    </DndContext>
  );
}

interface KanbanColumnProps {
  id: string;
  title: string;
  candidates: LeaderboardCandidateDto[];
  colorClass: string;
}

function KanbanColumn({ id, title, candidates, colorClass }: KanbanColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id });

  return (
    <div
      ref={setNodeRef}
      className={cn(
        "flex flex-col gap-3 p-3.5 rounded-xl border min-h-[380px] transition-all shrink-0 w-[260px] md:w-[280px]",
        colorClass.split(' ')[0],
        colorClass.split(' ')[1],
        isOver && "border-primary bg-primary/5 ring-1 ring-primary/25"
      )}
    >
      <div className="flex items-center justify-between border-b border-white/5 pb-2">
        <span className={cn("text-xs font-bold uppercase tracking-wider", colorClass.split(' ')[2])}>
          {title}
        </span>
        <span className="text-[10px] font-bold bg-white/5 px-2 py-0.5 rounded-full text-muted-foreground">
          {candidates.length}
        </span>
      </div>

      <div className="flex-1 flex flex-col gap-2 overflow-y-auto max-h-[400px] custom-scrollbar pr-1">
        {candidates.map((cand) => (
          <KanbanCard key={cand.applicationId} candidate={cand} />
        ))}
        {candidates.length === 0 && (
          <div className="flex-1 flex items-center justify-center text-[10px] text-muted-foreground italic text-center p-4 border border-dashed border-white/5 rounded-lg">
            Empty
          </div>
        )}
      </div>
    </div>
  );
}

function KanbanCard({ candidate }: { candidate: LeaderboardCandidateDto }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: candidate.applicationId,
    // Enable dragging only if candidate is Scored, SentToRecruiter, Shortlisted, or Rejected
    disabled: !['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(candidate.status),
  });

  const style = transform
    ? {
        transform: `translate3d(${transform.x}px, ${transform.y}px, 0)`,
        zIndex: 50,
      }
    : undefined;

  const isDraggable = ['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(candidate.status);

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className={cn(
        "p-3 rounded-lg border border-white/5 bg-white/[0.02] shadow-sm flex flex-col gap-2 group transition-all",
        isDraggable ? "cursor-grab active:cursor-grabbing hover:border-violet-500/30 hover:bg-white/[0.04]" : "opacity-60 cursor-not-allowed",
        isDragging && "opacity-30 border-primary"
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs font-semibold text-foreground leading-tight truncate group-hover:text-violet-400 transition-colors">
          {candidate.name}
        </span>
        
        {candidate.fitScore > 0 && (
          <span
            className={cn(
              "text-[10px] font-bold px-1.5 py-0.5 rounded-full leading-none shrink-0",
              candidate.fitScore >= 80 && "bg-emerald-500/10 text-emerald-400 border border-emerald-500/20",
              candidate.fitScore >= 60 && candidate.fitScore < 80 && "bg-amber-500/10 text-amber-400 border border-amber-500/20",
              candidate.fitScore < 60 && "bg-rose-500/10 text-rose-400 border border-rose-500/20"
            )}
          >
            {Math.round(candidate.fitScore)}
          </span>
        )}
      </div>
      
      {isDraggable && (
        <div className="flex items-center justify-between text-[9px] text-muted-foreground opacity-0 group-hover:opacity-100 transition-opacity">
          <span className="flex items-center gap-1">
            <ArrowRightLeft className="w-3 h-3" /> Drag to move
          </span>
        </div>
      )}
    </div>
  );
}
