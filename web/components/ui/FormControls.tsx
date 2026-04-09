import React from 'react';
import { Loader2 } from 'lucide-react';

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

export const Button = ({
  children,
  variant = 'primary',
  size = 'md',
  isLoading = false,
  leftIcon,
  rightIcon,
  className = '',
  disabled,
  ...props
}: ButtonProps) => {
  const baseStyles = 'inline-flex items-center justify-center rounded-lg font-medium transition-all focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed';
  
  const variants = {
    primary: 'bg-[var(--btn-blue-bg)] hover:bg-[var(--btn-blue-hover)] text-white shadow-sm',
    secondary: 'bg-[var(--btn-green-bg)] hover:bg-[var(--btn-green-hover)] text-white shadow-sm',
    outline: 'border border-[var(--border-color)] bg-transparent hover:bg-[var(--card-bg-hover)] text-[var(--foreground)]',
    ghost: 'bg-transparent hover:bg-[var(--card-bg-hover)] text-[var(--foreground)]',
    danger: 'bg-red-500 hover:bg-red-600 text-white shadow-sm',
  };

  const sizes = {
    sm: 'px-3 py-1.5 text-xs gap-1.5',
    md: 'px-4 py-2 text-sm gap-2',
    lg: 'px-6 py-3 text-base gap-2.5',
  };

  return (
    <button
      disabled={disabled || isLoading}
      className={`${baseStyles} ${variants[variant]} ${sizes[size]} ${className}`}
      {...props}
    >
      {isLoading && <Loader2 className="animate-spin" size={size === 'sm' ? 14 : 18} />}
      {!isLoading && leftIcon}
      {children}
      {!isLoading && rightIcon}
    </button>
  );
};

interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  containerClassName?: string;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

export const Input = ({ label, error, className = '', containerClassName = '', leftIcon, rightIcon, id, ...props }: InputProps) => {
  return (
    <div className={`flex flex-col gap-1.5 ${containerClassName}`}>
      {label && <label htmlFor={id} className="text-sm font-medium text-[var(--foreground)]">{label}</label>}
      <div className="relative flex items-center">
        {leftIcon && (
          <div className="absolute left-3 flex items-center pointer-events-none">
            {leftIcon}
          </div>
        )}
        <input
          id={id}
          {...props}
          className={`w-full px-3 py-2 border rounded-lg bg-[var(--background)] text-[var(--foreground)] text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all ${
            leftIcon ? 'pl-10' : ''
          } ${
            rightIcon ? 'pr-10' : ''
          } ${
            error ? 'border-red-400' : 'border-[var(--border-color)]'
          } ${className}`}
        />
        {rightIcon && (
          <div className="absolute right-3 flex items-center pointer-events-none">
            {rightIcon}
          </div>
        )}
      </div>
      {error && <p className="text-xs text-red-400">{error}</p>}
    </div>
  );
};

interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  error?: string;
  containerClassName?: string;
  children: React.ReactNode;
}

export const Select = ({ label, error, className = '', containerClassName = '', children, id, ...props }: SelectProps) => {
  return (
    <div className={`flex flex-col gap-1.5 ${containerClassName}`}>
      {label && <label htmlFor={id} className="text-sm font-medium text-[var(--foreground)]">{label}</label>}
      <select
        id={id}
        {...props}
        className={`px-3 py-2 border rounded-lg bg-[var(--background)] text-[var(--foreground)] text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all ${
          error ? 'border-red-400' : 'border-[var(--border-color)]'
        } ${className}`}
      >
        {children}
      </select>
      {error && <p className="text-xs text-red-400">{error}</p>}
    </div>
  );
};
