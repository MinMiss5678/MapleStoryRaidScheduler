import React from 'react';

interface CardProps {
  children: React.ReactNode;
  className?: string;
  hoverable?: boolean;
}

export const Card = ({ children, className = '', hoverable = false }: CardProps) => {
  return (
    <div className={`bg-[var(--card-bg)] border border-[var(--border-color)] rounded-2xl shadow-sm overflow-hidden transition-all ${
      hoverable ? 'hover:shadow-md hover:bg-[var(--card-bg-hover)]' : ''
    } ${className}`}>
      {children}
    </div>
  );
};

export const CardHeader = ({ children, className = '' }: { children: React.ReactNode; className?: string }) => (
  <div className={`px-6 py-4 border-b border-[var(--border-color)] ${className}`}>
    {children}
  </div>
);

export const CardContent = ({ children, className = '' }: { children: React.ReactNode; className?: string }) => (
  <div className={`p-6 ${className}`}>
    {children}
  </div>
);

export const CardFooter = ({ children, className = '' }: { children: React.ReactNode; className?: string }) => (
  <div className={`px-6 py-4 border-t border-[var(--border-color)] bg-muted/30 ${className}`}>
    {children}
  </div>
);
