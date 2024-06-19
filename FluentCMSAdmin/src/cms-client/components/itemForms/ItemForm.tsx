import {useForm} from "react-hook-form";
import {createInput} from "./inputs/createInput";

export function ItemForm({columns, data, id, onSubmit, formId, uploadUrl}: {
    columns: any[],
    data: any,
    id?: any
    onSubmit: any
    formId: any
    uploadUrl:any
    createInput: any
}) {
    const {
        register,
        handleSubmit,
        control
    } = useForm()

    return columns && <form onSubmit={handleSubmit(onSubmit)} id={formId}>
        <div className="formgrid grid">
            {
                columns.map((column: any) => createInput({data, column, register, control, id, uploadUrl}))
            }
        </div>
    </form>
}
