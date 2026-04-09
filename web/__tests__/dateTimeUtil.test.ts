import { describe, it, expect } from 'vitest'
import { formatDateTime, getDateTimeFromWeekday } from '../utils/dateTimeUtil'

describe('dateTimeUtil', () => {
  describe('formatDateTime', () => {
    it('should format date correctly with default locale', () => {
      const date = new Date('2024-03-20T10:30:00Z')
      // Note: testing locale string can be tricky due to environment differences,
      // but we can check if it contains expected parts
      const result = formatDateTime(date, 'UTC')
      expect(result).toContain('2024')
      expect(result).toContain('03')
      expect(result).toContain('20')
      expect(result).toContain('10:30')
    })
  })

  describe('getDateTimeFromWeekday', () => {
    it('should calculate correct date for weekday 1 (Monday)', () => {
      const startDate = '2024-03-18T00:00:00Z' // A Monday
      const result = getDateTimeFromWeekday(startDate, 1, '20:00')
      const resultDate = new Date(result)
      
      expect(resultDate.getFullYear()).toBe(2024)
      expect(resultDate.getMonth()).toBe(2) // March is 2
      expect(resultDate.getDate()).toBe(18)
      expect(resultDate.getHours()).toBe(20)
    })

    it('should calculate correct date for weekday 3 (Wednesday)', () => {
      const startDate = '2024-03-18T00:00:00Z' // A Monday
      const result = getDateTimeFromWeekday(startDate, 3, '21:30')
      const resultDate = new Date(result)
      
      expect(resultDate.getDate()).toBe(20)
      expect(resultDate.getHours()).toBe(21)
      expect(resultDate.getMinutes()).toBe(30)
    })
  })
})
