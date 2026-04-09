import { Availability, RegisterFormState } from "@/types/register";
import { TIMESLOTS } from "@/constants/register";
import { registerService } from "@/services/registerService";
import toast from "react-hot-toast";

interface UseTimeSelectionProps {
  form: RegisterFormState;
  updateForm: <K extends keyof RegisterFormState>(key: K, value: RegisterFormState[K]) => void;
  setForm: React.Dispatch<React.SetStateAction<RegisterFormState>>;
  setLoading: (loading: boolean) => void;
}

export function useTimeSelection({ form, updateForm, setForm, setLoading }: UseTimeSelectionProps) {
  const toggleAvailability = (weekday: number, timeslot: string, mode?: "add" | "remove") => {
    const startTime = timeslot + ":00";
    const [hours, minutes] = timeslot.split(':').map(Number);
    const endHours = hours + 1;
    const endTimeRaw = `${endHours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:00`;
    const endTime = endTimeRaw === "24:00:00" ? "00:00:00" : endTimeRaw;

    const exists = form.availabilities.some(a => a.weekday === weekday && a.timeslot === timeslot);

    if (mode === "add" && exists) return;
    if (mode === "remove" && !exists) return;

    let newAvailabilities: Availability[];
    if (exists && mode !== "add") {
      newAvailabilities = form.availabilities.filter(a => !(a.weekday === weekday && a.timeslot === timeslot));
    } else if (!exists && mode !== "remove") {
      newAvailabilities = [...form.availabilities, { weekday, timeslot, startTime, endTime }];
    } else {
      return;
    }
    updateForm("availabilities", newAvailabilities);
  };

  const quickFill = (type: "weekday_night" | "weekend_all" | "clear" | "invert" | "last_week") => {
    let newAvailabilities = [...form.availabilities];

    const generateRange = (w: number, startH: number, endH: number) => {
      const range = [];
      for (let h = startH; h < endH; h++) {
        const hMod = h % 24;
        const t = `${hMod.toString().padStart(2, '0')}:00`;
        if (TIMESLOTS.includes(t)) {
          const startTime = t + ":00";
          const endHMod = (hMod + 1) % 24;
          const endTime = `${endHMod.toString().padStart(2, '0')}:00:00`;
          range.push({ weekday: w, timeslot: t, startTime, endTime });
        }
      }
      return range;
    };

    if (type === "weekday_night") {
      for (let w = 1; w <= 5; w++) {
        const toAdd = generateRange(w, 20, 25).filter(a => !newAvailabilities.some(existing => existing.weekday === a.weekday && existing.timeslot === a.timeslot));
        newAvailabilities = [...newAvailabilities, ...toAdd];
      }
    } else if (type === "weekend_all") {
      [0, 6].forEach(w => {
        const toAdd = generateRange(w, 1, 25).filter(a => !newAvailabilities.some(existing => existing.weekday === a.weekday && existing.timeslot === a.timeslot));
        newAvailabilities = [...newAvailabilities, ...toAdd];
      });
    } else if (type === "clear") {
      newAvailabilities = [];
    } else if (type === "invert") {
      const inverted: Availability[] = [];
      for (let w = 0; w <= 6; w++) {
        for (const t of TIMESLOTS) {
          if (!newAvailabilities.some(a => a.weekday === w && a.timeslot === t)) {
            const [hours, minutes] = t.split(':').map(Number);
            const endHours = hours + 1;
            const endTimeRaw = `${endHours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:00`;
            const endTime = endTimeRaw === "24:00:00" ? "00:00:00" : endTimeRaw;
            inverted.push({
              weekday: w,
              timeslot: t,
              startTime: t + ":00",
              endTime: endTime
            });
          }
        }
      }
      newAvailabilities = inverted;
    } else if (type === "last_week") {
      const fillLastWeek = async () => {
        setLoading(true);
        try {
          const data = await registerService.getLastRegister();
          if (data) {
            const availabilities = data.availabilities.map((a: Availability) => {
              const timeslot = a.startTime.substring(0, 5);
              return {
                ...a,
                timeslot: timeslot,
                startTime: a.startTime.length === 5 ? a.startTime + ":00" : a.startTime,
                endTime: a.endTime.length === 5 ? a.endTime + ":00" : a.endTime
              };
            });
            setForm(prev => ({
              ...prev,
              availabilities: availabilities,
              characterRegisters: data.characterRegisters.map((cr) => ({
                characterId: cr.characterId,
                bossId: cr.bossId,
                rounds: cr.rounds
              }))
            }));
          } else {
            toast.error("找不到上週報名紀錄");
          }
        } catch (error) {
          toast.error(error instanceof Error ? error.message : "獲取紀錄失敗");
        } finally {
          setLoading(false);
        }
      };
      fillLastWeek();
      return;
    }

    updateForm("availabilities", newAvailabilities);
  };

  const copyDay = (fromWeekday: number) => {
    const sourceAvailabilities = form.availabilities.filter(a => a.weekday === fromWeekday);
    let newAvailabilities = [...form.availabilities];

    [0, 1, 2, 3, 4, 5, 6].filter(w => w !== fromWeekday).forEach(w => {
      newAvailabilities = newAvailabilities.filter(a => a.weekday !== w);
      const toAdd = sourceAvailabilities.map(a => ({ ...a, weekday: w }));
      newAvailabilities = [...newAvailabilities, ...toAdd];
    });

    updateForm("availabilities", newAvailabilities);
  };

  const handleWeekdayAllCheck = (weekday: number, checked: boolean) => {
    if (checked) {
      const toAdd = TIMESLOTS.map(t => {
        const [hours, minutes] = t.split(':').map(Number);
        const endHours = hours + 1;
        const endTimeRaw = `${endHours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:00`;
        const endTime = endTimeRaw === "24:00:00" ? "00:00:00" : endTimeRaw;
        return {
          weekday,
          timeslot: t,
          startTime: t + ":00",
          endTime: endTime
        };
      }).filter(t => !form.availabilities.some(a => a.weekday === weekday && a.timeslot === t.timeslot));
      updateForm("availabilities", [...form.availabilities, ...toAdd]);
    } else {
      updateForm("availabilities", form.availabilities.filter(a => a.weekday !== weekday));
    }
  };

  const handleTimeslotAllCheck = (timeslot: string, checked: boolean) => {
    if (checked) {
      const toAdd = [0, 1, 2, 3, 4, 5, 6].map(w => {
        const [hours, minutes] = timeslot.split(':').map(Number);
        const endHours = hours + 1;
        const endTimeRaw = `${endHours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:00`;
        const endTime = endTimeRaw === "24:00:00" ? "00:00:00" : endTimeRaw;
        return {
          weekday: w,
          timeslot,
          startTime: timeslot + ":00",
          endTime: endTime
        };
      }).filter(w => !form.availabilities.some(a => a.weekday === w.weekday && a.timeslot === timeslot));
      updateForm("availabilities", [...form.availabilities, ...toAdd]);
    } else {
      updateForm("availabilities", form.availabilities.filter(a => a.timeslot !== timeslot));
    }
  };

  return {
    toggleAvailability,
    quickFill,
    copyDay,
    handleWeekdayAllCheck,
    handleTimeslotAllCheck
  };
}
