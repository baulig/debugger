#include <mono-runtime.h>

#define MONO_DEBUGGER_MAJOR_VERSION                     81
#define MONO_DEBUGGER_MINOR_VERSION                     6
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
	gsize init_code_buffer;

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

static bool initialize_breakpoint_handler (MdbInferior *inferior, BreakpointInfo *breakpoint);

class MonoRuntimeImpl : public MonoRuntime
{
protected:
	MonoRuntimeImpl (MdbProcess *process, MdbExeReader *exe, gsize info_ptr)
		: MonoRuntime (process, exe)
	{
		this->debugger_info_ptr = info_ptr;
		this->debugger_info = NULL;
		this->initialize_bpt = NULL;
		this->executable_code_buffer;
		this->thread_abort_signal = 0;
	}

	bool Initialize (MdbInferior *inferior);

private:
	gsize debugger_info_ptr;
	MonoDebuggerInfo *debugger_info;

	BreakpointInfo *initialize_bpt;

	gsize executable_code_buffer;
	guint32 thread_abort_signal;

	void OnInitializeBreakpoint (MdbInferior *inferior);
	friend bool initialize_breakpoint_handler (MdbInferior *inferior, BreakpointInfo *breakpoint);

	friend MonoRuntime *MonoRuntime::Initialize (MdbInferior *inferior, MdbExeReader *exe);

	ErrorCode ProcessCommand (int command, int id, Buffer *in, Buffer *out);

	gsize GetNotificationAddress (void)
	{
		return debugger_info->notification_function_ptr;
	}

	gsize GetGenericInvocationFunc (void)
	{
		return debugger_info->generic_invocation_func;
	}
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

	if (inferior->PeekWord (debugger_info->executable_code_buffer_ptr, &executable_code_buffer))
		return;

	if (inferior->ReadMemory (debugger_info->thread_abort_signal_ptr, 4, &thread_abort_signal))
		return;

	g_message (G_STRLOC ": %Lx - %Lx", executable_code_buffer, debugger_info->notification_function_ptr);
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

	g_message (G_STRLOC);

	if (inferior->InsertBreakpoint (debugger_info->initialize_ptr, &initialize_bpt)) {
		g_warning (G_STRLOC);
		return false;
	}

	initialize_bpt->handler = initialize_breakpoint_handler;
	initialize_bpt->user_data = this;

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

		out->AddLong (debugger_info->init_code_buffer);
		out->AddLong (debugger_info->create_string_ptr);

		out->AddInt (debugger_info->mono_trampoline_num);
		out->AddLong (debugger_info->mono_trampoline_code_ptr);

		out->AddLong (debugger_info->symbol_table_ptr);
		out->AddInt (debugger_info->symbol_table_size);

		out->AddLong (debugger_info->metadata_info_ptr);

		out->AddLong (debugger_info->generic_invocation_func);
		break;

	default:
		return ERR_NOT_IMPLEMENTED;
	}

	return ERR_NONE;
}
