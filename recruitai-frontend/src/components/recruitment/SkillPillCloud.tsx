'use client';

import React, { useState } from 'react';
import { ThumbsUp, ThumbsDown, Plus, X } from 'lucide-react';
import { SkillGraph, SkillWeight } from '@/types';
import { cn } from '@/lib/utils';

interface SkillPillCloudProps {
  skills: SkillGraph;
  onChange: (updatedSkills: SkillGraph) => void;
}

export function SkillPillCloud({ skills, onChange }: SkillPillCloudProps) {
  const [feedback, setFeedback] = useState<'up' | 'down' | null>(null);
  const [editing, setEditing] = useState(false);
  const [newSkillName, setNewSkillName] = useState('');
  const [newSkillCategory, setNewSkillCategory] = useState<'required' | 'niceToHave' | 'domain'>('required');

  const handleThumbsUp = () => {
    setFeedback('up');
    setEditing(false);
  };

  const handleThumbsDown = () => {
    setFeedback('down');
    setEditing(true);
  };

  const removeRequiredSkill = (index: number) => {
    const nextRequired = [...skills.requiredSkills];
    nextRequired.splice(index, 1);
    onChange({ ...skills, requiredSkills: nextRequired });
  };

  const removeNiceToHaveSkill = (index: number) => {
    const nextNice = [...skills.niceToHaveSkills];
    nextNice.splice(index, 1);
    onChange({ ...skills, niceToHaveSkills: nextNice });
  };

  const removeDomainKeyword = (index: number) => {
    const nextDomain = [...skills.domainKeywords];
    nextDomain.splice(index, 1);
    onChange({ ...skills, domainKeywords: nextDomain });
  };

  const handleAddSkill = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newSkillName.trim()) return;

    if (newSkillCategory === 'required') {
      const newItem: SkillWeight = {
        skill: newSkillName.trim(),
        weight: 0.8,
        category: 'general',
      };
      onChange({
        ...skills,
        requiredSkills: [...skills.requiredSkills, newItem],
      });
    } else if (newSkillCategory === 'niceToHave') {
      const newItem: SkillWeight = {
        skill: newSkillName.trim(),
        weight: 0.5,
        category: 'general',
      };
      onChange({
        ...skills,
        niceToHaveSkills: [...skills.niceToHaveSkills, newItem],
      });
    } else {
      onChange({
        ...skills,
        domainKeywords: [...skills.domainKeywords, newSkillName.trim()],
      });
    }

    setNewSkillName('');
  };

  return (
    <div className="flex flex-col gap-4 p-5 rounded-xl border border-white/5 bg-white/5 animate-in fade-in slide-in-from-top-2 duration-300">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h4 className="text-sm font-semibold">AI Skill Graph Profile</h4>
          <p className="text-xs text-muted-foreground leading-relaxed mt-0.5">
            Preview of skills extracted automatically from the description
          </p>
        </div>

        {/* Feedback triggers */}
        <div className="flex items-center gap-2">
          <span className="text-xs text-muted-foreground">Looks accurate?</span>
          <button
            type="button"
            onClick={handleThumbsUp}
            className={cn(
              "p-2 rounded-lg border transition-all",
              feedback === 'up'
                ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400"
                : "border-white/5 bg-white/5 text-muted-foreground hover:text-foreground hover:bg-white/10"
            )}
          >
            <ThumbsUp className="w-4 h-4" />
          </button>
          <button
            type="button"
            onClick={handleThumbsDown}
            className={cn(
              "p-2 rounded-lg border transition-all",
              feedback === 'down'
                ? "border-amber-500/30 bg-amber-500/10 text-amber-400"
                : "border-white/5 bg-white/5 text-muted-foreground hover:text-foreground hover:bg-white/10"
            )}
          >
            <ThumbsDown className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Cloud pills */}
      <div className="space-y-4">
        {/* Required skills */}
        {skills.requiredSkills.length > 0 && (
          <div className="space-y-1.5">
            <span className="text-[10px] font-bold text-violet-400 uppercase tracking-wider">Required Skills</span>
            <div className="flex flex-wrap gap-2">
              {skills.requiredSkills.map((s, idx) => (
                <div
                  key={`req-${idx}`}
                  className="flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-violet-500/10 border border-violet-500/20 text-violet-300 shadow-sm"
                >
                  <span>{s.skill}</span>
                  <span className="text-[10px] opacity-65 bg-violet-500/20 px-1 py-0.5 rounded">
                    {Math.round(s.weight * 100)}%
                  </span>
                  {editing && (
                    <button
                      type="button"
                      onClick={() => removeRequiredSkill(idx)}
                      className="hover:text-rose-400 transition-colors"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Nice to have */}
        {skills.niceToHaveSkills.length > 0 && (
          <div className="space-y-1.5">
            <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Nice-to-Have Skills</span>
            <div className="flex flex-wrap gap-2">
              {skills.niceToHaveSkills.map((s, idx) => (
                <div
                  key={`nice-${idx}`}
                  className="flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-slate-500/10 border border-slate-500/20 text-slate-300"
                >
                  <span>{s.skill}</span>
                  {editing && (
                    <button
                      type="button"
                      onClick={() => removeNiceToHaveSkill(idx)}
                      className="hover:text-rose-400 transition-colors"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Domain keywords */}
        {skills.domainKeywords.length > 0 && (
          <div className="space-y-1.5">
            <span className="text-[10px] font-bold text-amber-400 uppercase tracking-wider">Domain Keywords</span>
            <div className="flex flex-wrap gap-2">
              {skills.domainKeywords.map((k, idx) => (
                <div
                  key={`domain-${idx}`}
                  className="flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-amber-500/10 border border-amber-500/20 text-amber-300"
                >
                  <span>{k}</span>
                  {editing && (
                    <button
                      type="button"
                      onClick={() => removeDomainKeyword(idx)}
                      className="hover:text-rose-400 transition-colors"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Editing tag input interface */}
      {editing && (
        <form onSubmit={handleAddSkill} className="flex flex-wrap items-center gap-2 pt-3 border-t border-white/5">
          <input
            type="text"
            placeholder="Add custom skill or keyword..."
            value={newSkillName}
            onChange={(e) => setNewSkillName(e.target.value)}
            className="flex-1 min-w-[200px] bg-white/5 border border-white/10 rounded-lg px-3 py-1.5 text-xs focus:outline-none focus:border-violet-500"
          />
          <select
            value={newSkillCategory}
            onChange={(e: any) => setNewSkillCategory(e.target.value)}
            className="bg-white/5 border border-white/10 rounded-lg px-2 py-1.5 text-xs focus:outline-none"
          >
            <option value="required">Required</option>
            <option value="niceToHave">Nice-to-Have</option>
            <option value="domain">Domain Keyword</option>
          </select>
          <button
            type="submit"
            className="bg-violet-600 hover:bg-violet-500 text-white rounded-lg p-2 transition-colors flex items-center gap-1.5 text-xs font-semibold"
          >
            <Plus className="w-3.5 h-3.5" /> Add tag
          </button>
        </form>
      )}
    </div>
  );
}
