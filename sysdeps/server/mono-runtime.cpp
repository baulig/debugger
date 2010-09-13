#include <mono-runtime.h>
#include <mdb-arch.h>

#define MONO_DEBUGGER_MAJOR_VERSION                     82
#define MONO_DEBUGGER_MINOR_VERSION                     0
#define MONO_DEBUGGER_MAGIC                             0x7aff65af4253d427ULL

typedef struct {
	guint64 magic;
	guint32 major_version;
	guint32 minor_version;
	guint32 runtime_flags;
	guint32 total_size;
} MonoDebuggerInfoHeader;

typedef struct {
	guint64 magic;
	guint32 major_version;
	guint32 minor_version;
	guint32 runtime_flags;
	guint32 total_size;
	guint32 symbol_table_size;
	guint32 mono_trampoline_num;
	gsize mono_trampoline_code_ptr;
	gsize notification_function_ptr;
	gsize symbol_table_ptr;
	gsize metadata_info_ptr;
	gsize debugger_version_ptr;

	gsize compile_method_ptr;
	gsize get_virtual_method_ptr;
	gsize get_boxed_object_method_ptr;
	gsize runtime_invoke_ptr;
	gsize class_get_static_field_data_ptr;
	gsize run_finally_ptr;
	gsize initialize_ptr;

	gsize create_string_ptr;
	gsize lookup_class_ptr;

	gsize insert_method_breakpoint_ptr;
	gsize insert_source_breakpoint_ptr;
	gsize remove_breakpoint_ptr;

	gsize register_class_init_callback_ptr;
	gsize remove_class_init_callback_ptr;

	gsize thread_table_ptr;

	gsize executable_code_buffer_ptr;
	gsize mono_breakpoint_info_ptr;
	gsize mono_breakpoint_info_index_ptr;

	guint32 executable_code_buffer_size;
	guint32 breakpoint_array_size;

	gsize get_method_signature_ptr;
	gsize init_code_buffer_ptr;

#if !defined(HOST_WIN32)
	gsize thread_vtable_ptr_ptr;
	gsize debugger_thread_vtable_ptr;
#endif
	gsize event_handler_ptr_ptr;
	gsize debugger_event_handler_ptr;
	gsize using_mono_debugger_ptr;
	gsize interruption_request_ptr;

	gsize abort_runtime_invoke_ptr;

	gsize thread_abort_signal_ptr;

	gsize generic_invocation_func;
} MonoDebuggerInfo;

typedef struct {
	int size;
	int mono_defaults_size;
	gsize mono_defaults_ptr;
	int type_size;
	int array_type_size;
	int klass_size;
	int thread_size;
	int thread_tid_offset;
	int thread_stack_ptr_offset;
	int thread_end_stack_offset;
	int klass_image_offset;
	int klass_instance_size_offset;
	int klass_parent_offset;
	int klass_token_offset;
	int klass_field_offset;
	int klass_methods_offset;
	int klass_method_count_offset;
	int klass_this_arg_offset;
	int klass_byval_arg_offset;
	int klass_generic_class_offset;
	int klass_generic_container_offset;
	int klass_vtable_offset;
	int field_info_size;
	int field_info_type_offset;
	int field_info_offset_offset;
	int mono_defaults_corlib_offset;
	int mono_defaults_object_offset;
	int mono_defaults_byte_offset;
	int mono_defaults_void_offset;
	int mono_defaults_boolean_offset;
	int mono_defaults_sbyte_offset;
	int mono_defaults_int16_offset;
	int mono_defaults_uint16_offset;
	int mono_defaults_int32_offset;
	int mono_defaults_uint32_offset;
	int mono_defaults_int_offset;
	int mono_defaults_uint_offset;
	int mono_defaults_int64_offset;
	int mono_defaults_uint64_offset;
	int mono_defaults_single_offset;
	int mono_defaults_double_offset;
	int mono_defaults_char_offset;
	int mono_defaults_string_offset;
	int mono_defaults_enum_offset;
	int mono_defaults_array_offset;
	int mono_defaults_delegate_offset;
	int mono_defaults_exception_offset;
	int mono_method_klass_offset;
	int mono_method_token_offset;
	int mono_method_flags_offset;
	int mono_method_inflated_offset;
	int mono_vtable_klass_offset;
	int mono_vtable_vtable_offset;
} MonoDebuggerMetadataInfo;

typedef struct {
	gsize thread_info_ptr;
	gsize tid, lmf_addr;
} RuntimeInferiorData;

class MonoRuntimeImpl;

typedef struct {
	MonoRuntimeImpl *runtime;
	guint8 *instruction;
	int insn_size;
	bool update_ip;
	gsize code_address;
	int slot;
} ExecuteInsnData;

#define EXECUTABLE_CODE_CHUNK_SIZE		16

static bool initialize_breakpoint_handler (MdbInferior *inferior, BreakpointInfo *breakpoint);
static void execute_insn_handler (MdbInferior *inferior, gsize arg1, gsize arg2, gpointer user_data);
static void execute_insn_done (MdbInferior *inferior, gsize rip, G_GNUC_UNUSED gsize dummy, gpointer user_data);

class MonoRuntimeImpl : public MonoRuntime
{
public:
	void HandleNotification (MdbInferior *inferior, NotificationType type, gsize arg1, gsize arg2);

	ErrorCode SetExtendedNotifications (MdbInferior *inferior, NotificationType type, bool enable);

	void OnInitializeBreakpoint (MdbInferior *inferior);

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

	gsize GetNotificationAddress (void)
	{
		return debugger_info->notification_function_ptr;
	}

	gsize GetGenericInvocationFunc (void)
	{
		return debugger_info->generic_invocation_func;
	}

	ErrorCode EnableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint);
	ErrorCode DisableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint);

	ErrorCode ExecuteInstruction (MdbInferior *inferior, const guint8 *instruction,
				      guint32 size, bool update_ip);

protected:
	MonoRuntimeImpl (MdbProcess *process, MdbExeReader *exe, gsize info_ptr)
		: MonoRuntime (process, exe)
	{
		this->debugger_info_ptr = info_ptr;
		this->debugger_info = NULL;
		this->initialize_bpt = NULL;
		this->executable_code_buffer_ptr = 0;
		this->thread_abort_signal = 0;

		this->inferior_data_hash = g_hash_table_new (NULL, NULL);
	}

	bool Initialize (MdbInferior *inferior);

	friend bool initialize_breakpoint_handler (MdbInferior *inferior, BreakpointInfo *breakpoint);
	friend MonoRuntime *MonoRuntime::Initialize (MdbInferior *inferior, MdbExeReader *exe);

private:
	gsize debugger_info_ptr;
	MonoDebuggerInfo *debugger_info;

	BreakpointInfo *initialize_bpt;

	guint32 thread_abort_signal;

	GHashTable *inferior_data_hash;

	guint8 *breakpoint_table_bitfield;
	int FindBreakpointTableSlot (void);

	gsize executable_code_buffer_ptr;
	guint32 executable_code_chunk_size;
	guint32 executable_code_total_chunks;
	guint8 *executable_code_bitfield;
	guint32 executable_code_last_slot;

	int FindCodeBufferSlot (void);

	friend void execute_insn_handler (MdbInferior *inferior, gsize arg1, gsize arg2, gpointer user_data);
	friend void execute_insn_done (MdbInferior *inferior, gsize rip, G_GNUC_UNUSED gsize dummy, gpointer user_data);

	ErrorCode DoExecuteInstruction (MdbInferior *inferior, ExecuteInsnData *data);
};

MonoRuntime *
MonoRuntime::Initialize (MdbInferior *inferior, MdbExeReader *exe)
{
	MonoDebuggerInfoHeader header;
	gsize debugger_info_ptr;
	guint64 address;
	MonoRuntimeImpl *runtime;

	g_message (G_STRLOC ": %s", exe->GetFileName ());

	address = exe->GetSectionAddress (".mdb_debug_info");
	g_message (G_STRLOC ": %Lx", (gsize) address);
	if (!address)
		return NULL;

	if (inferior->PeekWord (address, &debugger_info_ptr))
		return NULL;
	g_message (G_STRLOC ": %Lx - %Lx", address, debugger_info_ptr);
	if (!debugger_info_ptr)
		return NULL;

	if (inferior->ReadMemory (debugger_info_ptr, sizeof (header), &header))
		return NULL;

	if (header.magic != MONO_DEBUGGER_MAGIC)
		return NULL;
	if (header.major_version != MONO_DEBUGGER_MAJOR_VERSION)
		return NULL;
	if (header.minor_version != MONO_DEBUGGER_MINOR_VERSION)
		return NULL;
	if (header.total_size < sizeof (MonoDebuggerInfo))
		return NULL;

	runtime = new MonoRuntimeImpl (inferior->GetProcess (), exe, debugger_info_ptr);
	if (!runtime->Initialize (inferior)) {
		delete runtime;
		return NULL;
	}

	return runtime;
}

void
MonoRuntimeImpl::OnInitializeBreakpoint (MdbInferior *inferior)
{
	g_message (G_STRLOC);

	if (inferior->PeekWord (debugger_info->executable_code_buffer_ptr, &executable_code_buffer_ptr))
		return;
	if (inferior->ReadMemory (debugger_info->thread_abort_signal_ptr, 4, &thread_abort_signal))
		return;

	g_message (G_STRLOC ": %Lx - %Lx", executable_code_buffer_ptr, debugger_info->notification_function_ptr);
}

static bool
initialize_breakpoint_handler (MdbInferior *inferior, BreakpointInfo *breakpoint)
{
	MonoRuntimeImpl *runtime = (MonoRuntimeImpl *) breakpoint->user_data;
	runtime->OnInitializeBreakpoint (inferior);
	return true;
}

bool
MonoRuntimeImpl::Initialize (MdbInferior *inferior)
{
	debugger_info = g_new0 (MonoDebuggerInfo, 1);
	if (inferior->ReadMemory (debugger_info_ptr, sizeof (MonoDebuggerInfo), debugger_info)) {
		g_free (debugger_info);
		debugger_info = NULL;
		return false;
	}

	if (inferior->PokeWord (debugger_info->using_mono_debugger_ptr, 1)) {
		g_warning (G_STRLOC ": %Lx", debugger_info->using_mono_debugger_ptr);
		return false;
	}

	if (inferior->InsertBreakpoint (debugger_info->initialize_ptr, &initialize_bpt)) {
		g_warning (G_STRLOC);
		return false;
	}

	initialize_bpt->handler = initialize_breakpoint_handler;
	initialize_bpt->user_data = this;

	breakpoint_table_bitfield = (guint8 *) g_malloc0 (debugger_info->breakpoint_array_size);

	executable_code_chunk_size = EXECUTABLE_CODE_CHUNK_SIZE;
	executable_code_total_chunks = debugger_info->executable_code_buffer_size / EXECUTABLE_CODE_CHUNK_SIZE;
	executable_code_bitfield = (guint8 *) g_malloc0 (executable_code_total_chunks);

	return true;
}

ErrorCode
MonoRuntimeImpl::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	switch (command) {
	case CMD_MONO_RUNTIME_GET_DEBUGGER_INFO:
		out->AddInt (debugger_info->major_version);
		out->AddInt (debugger_info->minor_version);

		out->AddLong (debugger_info->insert_method_breakpoint_ptr);
		out->AddLong (debugger_info->insert_source_breakpoint_ptr);
		out->AddLong (debugger_info->remove_breakpoint_ptr);

		out->AddLong (debugger_info->lookup_class_ptr);
		out->AddLong (debugger_info->class_get_static_field_data_ptr);
		out->AddLong (debugger_info->get_method_signature_ptr);
		out->AddLong (debugger_info->get_virtual_method_ptr);
		out->AddLong (debugger_info->get_boxed_object_method_ptr);

		out->AddLong (debugger_info->compile_method_ptr);
		out->AddLong (debugger_info->runtime_invoke_ptr);
		out->AddLong (debugger_info->abort_runtime_invoke_ptr);
		out->AddLong (debugger_info->run_finally_ptr);

		out->AddLong (debugger_info->init_code_buffer_ptr);
		out->AddLong (debugger_info->create_string_ptr);

		out->AddInt (debugger_info->mono_trampoline_num);
		out->AddLong (debugger_info->mono_trampoline_code_ptr);

		out->AddLong (debugger_info->symbol_table_ptr);
		out->AddInt (debugger_info->symbol_table_size);

		out->AddLong (debugger_info->metadata_info_ptr);

		out->AddLong (debugger_info->generic_invocation_func);
		break;

	case CMD_MONO_RUNTIME_SET_EXTENDED_NOTIFICATIONS: {
		MdbInferior *inferior;
		NotificationType type;
		bool enable;
		int iid;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		type = (NotificationType) in->DecodeInt ();
		enable = in->DecodeByte () != 0;
		return SetExtendedNotifications (inferior, type, enable);
	}

	case CMD_MONO_RUNTIME_EXECUTE_INSTRUCTION: {
		MdbInferior *inferior;
 		const guint8 *instruction;
		bool update_ip;
		int iid, size;

		iid = in->DecodeID ();
		inferior = (MdbInferior *) ServerObject::GetObjectByID (iid, SERVER_OBJECT_KIND_INFERIOR);

		if (!inferior)
			return ERR_NO_SUCH_INFERIOR;

		size = in->DecodeInt ();
		instruction = in->DecodeBuffer (size);

		update_ip = in->DecodeByte() != 0;

		return ExecuteInstruction (inferior, instruction, size, update_ip);
	}

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}

int
MonoRuntimeImpl::FindBreakpointTableSlot ()
{
	guint32 i;

	for (i = 1; i < debugger_info->breakpoint_array_size; i++) {
		if (breakpoint_table_bitfield [i])
			continue;

		breakpoint_table_bitfield [i] = 1;
		return i;
	}

	return -1;
}

ErrorCode
MonoRuntimeImpl::EnableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint)
{
	guint64 table_address, index_address;
	ErrorCode result;
	int slot;

	slot = FindBreakpointTableSlot ();
	if (slot < 0)
		return ERR_INTERNAL_ERROR;

	breakpoint->runtime_table_slot = slot;

	table_address = debugger_info->mono_breakpoint_info_ptr + 2 * sizeof (gsize) * slot;
	index_address = debugger_info->mono_breakpoint_info_index_ptr + sizeof (gsize) * slot;

	result = inferior->PokeWord (table_address, breakpoint->address);
	if (result)
		return result;

	result = inferior->PokeWord (table_address + 4, (gsize) breakpoint->saved_insn);
	if (result)
		return result;

	result = inferior->PokeWord (index_address, (gsize) slot);
	if (result)
		return result;

	return ERR_NONE;
}

ErrorCode
MonoRuntimeImpl::DisableBreakpoint (MdbInferior *inferior, BreakpointInfo *breakpoint)
{
	guint64 index_address;
	ErrorCode result;
	int slot;

	slot = breakpoint->runtime_table_slot;
	index_address = debugger_info->mono_breakpoint_info_index_ptr + sizeof (gsize) * slot;

	result = inferior->PokeWord (index_address, 0);
	if (result)
		return result;

	breakpoint_table_bitfield [slot] = 0;

	return ERR_NONE;
}

int
MonoRuntimeImpl::FindCodeBufferSlot (void)
{
	guint32 i;

	for (i = executable_code_last_slot + 1; i < executable_code_total_chunks; i++) {
		if (executable_code_bitfield [i])
			continue;

		executable_code_bitfield [i] = 1;
		executable_code_last_slot = i;
		return i;
	}

	executable_code_last_slot = 0;
	for (i = 0; i < executable_code_total_chunks; i++) {
		if (executable_code_bitfield [i])
			continue;

		executable_code_bitfield [i] = 1;
		executable_code_last_slot = i;
		return i;
	}

	return -1;
}

static void
execute_insn_handler (MdbInferior *inferior, gsize arg1, gsize arg2, gpointer user_data)
{
	ExecuteInsnData *data = (ExecuteInsnData *) user_data;
	ErrorCode result;

	g_message (G_STRLOC ": %Lx - %Lx", arg1, arg2);

	data->runtime->executable_code_buffer_ptr = arg1;

	result = data->runtime->DoExecuteInstruction (inferior, data);

	g_message (G_STRLOC ": %d", result);
}

static void
execute_insn_done (MdbInferior *inferior, gsize rip, G_GNUC_UNUSED gsize dummy, gpointer user_data)
{
	ExecuteInsnData *data = (ExecuteInsnData *) user_data;

	g_message (G_STRLOC);

	data->runtime->executable_code_bitfield [data->slot] = 0;

	g_free (data->instruction);
	g_free (data);
}

ErrorCode
MonoRuntimeImpl::ExecuteInstruction (MdbInferior *inferior, const guint8 *instruction,
				     guint32 size, bool update_ip)
{
	ExecuteInsnData *data;
	ErrorCode result;

	if (size > executable_code_chunk_size)
		return ERR_UNKNOWN_ERROR;

	data = g_new0 (ExecuteInsnData, 1);

	data->runtime = this;
	data->instruction = (guint8 *) g_memdup (instruction, size);
	data->insn_size = size;
	data->update_ip = update_ip;

	if (!executable_code_buffer_ptr) {
		InvocationData *invocation = g_new0 (InvocationData, 1);

		g_message (G_STRLOC ": %Lx", debugger_info->init_code_buffer_ptr);

		invocation->type = INVOCATION_TYPE_LONG_LONG;
		invocation->method_address = debugger_info->init_code_buffer_ptr;
		invocation->callback = new InferiorCallback (execute_insn_handler, data);

		result = inferior->CallMethod (invocation);
		if (result)
			return result;

		return ERR_NONE;
	}

	return DoExecuteInstruction (inferior, data);
}

ErrorCode
MonoRuntimeImpl::DoExecuteInstruction (MdbInferior *inferior, ExecuteInsnData *data)
{
	ErrorCode result;
	int slot;

	if (!executable_code_buffer_ptr)
		return ERR_NO_CODE_BUFFER;

	slot = FindCodeBufferSlot ();
	if (slot < 0)
		return ERR_UNKNOWN_ERROR;

	data->code_address = executable_code_buffer_ptr + slot * executable_code_chunk_size;

	result = inferior->WriteMemory (data->code_address, data->insn_size, data->instruction);
	if (result)
		return result;

	return inferior->GetArch ()->ExecuteInstruction (
		inferior, data->code_address, data->insn_size, data->update_ip,
		new InferiorCallback (execute_insn_done, data));
}

ErrorCode
MonoRuntimeImpl::SetExtendedNotifications (MdbInferior *inferior, NotificationType type, bool enable)
{
	RuntimeInferiorData *data;
	gsize notifications;
	ErrorCode result;

	data = (RuntimeInferiorData *) g_hash_table_lookup (inferior_data_hash, inferior);
	if (!data)
		return ERR_NO_SUCH_INFERIOR;

	result = inferior->PeekWord (data->thread_info_ptr + 24, &notifications);
	if (result)
		return result;

	if (enable)
		notifications |= (gsize) type;
	else
		notifications &= ~(gsize) type;

	return inferior->PokeWord (data->thread_info_ptr + 24, notifications);
}

void
MonoRuntimeImpl::HandleNotification (MdbInferior *inferior, NotificationType type, gsize arg1, gsize arg2)
{
	g_message (G_STRLOC ": HandleNotification(): %d - %Lx - %Lx", type, arg1, arg2);

	switch (type) {
	case NOTIFICATION_THREAD_CREATED: {
		RuntimeInferiorData *data = g_new0 (RuntimeInferiorData, 1);

		data->thread_info_ptr = arg2;

		if (inferior->PeekWord (arg2, &data->tid) || inferior->PeekWord (arg2 + 8, &data->lmf_addr)) {
			g_warning (G_STRLOC);
			g_free (data);
			break;
		}

		g_message (G_STRLOC ": %p - %Lx - %Lx - %Lx", data, data->thread_info_ptr, data->tid, data->lmf_addr);
		g_hash_table_insert (inferior_data_hash, inferior, data);
		break;
	}

	default:
		break;
	}
}

