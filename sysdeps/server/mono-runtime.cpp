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

class MonoRuntimeImpl : public MonoRuntime
{
protected:
	MonoRuntimeImpl (MdbProcess *process, MdbExeReader *exe, gsize info_ptr)
		: MonoRuntime (process, exe)
	{
		this->debugger_info_ptr = info_ptr;
		this->debugger_info = NULL;
	}

	bool Initialize (MdbInferior *inferior);

private:
	gsize debugger_info_ptr;
	MonoDebuggerInfo *debugger_info;

	friend MonoRuntime *MonoRuntime::Initialize (MdbInferior *inferior, MdbExeReader *exe);
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

bool
MonoRuntimeImpl::Initialize (MdbInferior *inferior)
{
	debugger_info = g_new0 (MonoDebuggerInfo, 1);
	if (inferior->ReadMemory (debugger_info_ptr, sizeof (MonoDebuggerInfo), debugger_info)) {
		g_free (debugger_info);
		debugger_info = NULL;
		return false;
	}

	return true;
}

ErrorCode
MonoRuntime::ProcessCommand (int command, int id, Buffer *in, Buffer *out)
{
	return ERR_NOT_IMPLEMENTED;
}
