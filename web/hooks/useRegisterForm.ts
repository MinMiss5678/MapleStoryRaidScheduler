import { useState, useEffect, useCallback } from "react";
import { useRouter } from "next/navigation";
import { useLoading } from "@/app/providers/LoadingContext";
import { useQueryClient } from "@tanstack/react-query";
import toast from "react-hot-toast";
import { CharacterRegister, Availability, RegisterFormState } from "@/types/register";
import { registerService } from "@/services/registerService";
import { useCharacters } from "./queries/useCharacters";
import { useBosses } from "./queries/useBosses";
import { usePeriod } from "./queries/usePeriod";
import { useRegisterData } from "./queries/useRegisterData";
import { useTimeSelection } from "./useTimeSelection";
import { useBossAssignment } from "./useBossAssignment";

export function useRegisterForm() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { setLoading } = useLoading();

    const { data: characters = [] } = useCharacters();
    const { data: bosses = [] } = useBosses();
    const { data: period } = usePeriod();
    const { data: registerData, isLoading: isRegisterLoading } = useRegisterData();

    const [form, setForm] = useState<RegisterFormState>({
        periodId: null,
        availabilities: [],
        characterRegisters: [],
        deleteCharacterRegisterIds: []
    });

    // 報名截止時間（後端權威值）。前端據此在過截止時擋送出，讓玩家不用填完才撞後端錯誤。
    // 判斷與後端 EnsureRegistrationOpen 一致：沒有 deadline（無 active period）或尚未過 → 開放。
    const [deadline, setDeadline] = useState<string | null>(null);
    useEffect(() => {
        registerService.getDeadline().then(setDeadline).catch(() => setDeadline(null));
    }, []);
    const isRegistrationOpen = !deadline || new Date(deadline).getTime() > Date.now();

    const updateForm = useCallback(<K extends keyof RegisterFormState>(key: K, value: RegisterFormState[K]) => {
        setForm(prev => ({ ...prev, [key]: value }));
    }, []);

    // 初始資料填充
    useEffect(() => {
        if (period) {
            updateForm("periodId", period.id);
        }
    }, [period, updateForm]);

    useEffect(() => {
        if (registerData) {
            setForm({
                id: registerData.id,
                periodId: registerData.periodId,
                availabilities: registerData.availabilities.map((a: Availability) => ({
                    ...a,
                    timeslot: a.startTime.substring(0, 5)
                })),
                characterRegisters: registerData.characterRegisters.map((cr: CharacterRegister) => ({
                    id: cr.id,
                    characterId: cr.characterId,
                    bossId: cr.bossId,
                    rounds: cr.rounds
                })),
                deleteCharacterRegisterIds: []
            });
        }
    }, [registerData]);

    const timeSelection = useTimeSelection({ form, updateForm, setForm, setLoading });
    const bossAssignment = useBossAssignment({ form, updateForm, setForm, bosses, characters });

    const onSubmit = async () => {
        // 前端防線：過截止就不送出（送出鈕也會 disabled，這裡是 belt-and-suspenders）。
        // 後端仍會擋（BusinessException → 400），此處只是省一趟壞送出、給即時回饋。
        if (!isRegistrationOpen) {
            toast.error("目前已超過報名截止時間。");
            return;
        }
        setLoading(true);
        try {
            if (form.id) {
                await registerService.updateRegister(form);
                toast.success("修改成功");
            } else {
                await registerService.createRegister(form);
                toast.success("報名成功");
            }
            await queryClient.invalidateQueries({ queryKey: ["register"] });
            router.push("/");
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "提交失敗");
        } finally {
            setLoading(false);
        }
    };

    const cancelRegister = async () => {
        if (!form.id) return;
        if (!confirm("確定要取消報名嗎？")) return;

        setLoading(true);
        try {
            await registerService.deleteRegister(form.id);
            toast.success("已取消報名");
            await queryClient.invalidateQueries({ queryKey: ["register"] });
            router.push("/");
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "取消失敗");
        } finally {
            setLoading(false);
        }
    };

    return {
        form,
        characters,
        bosses,
        period,
        isRegisterLoading,
        isRegistrationOpen,
        updateForm,
        ...timeSelection,
        ...bossAssignment,
        onSubmit,
        cancelRegister
    };
}
