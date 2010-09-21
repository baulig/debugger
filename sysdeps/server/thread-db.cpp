#include <thread-db.h>

/* Functions in this interface return one of these status codes.  */
typedef enum
{
	PS_OK,		/* Generic "call succeeded".  */
	PS_ERR,		/* Generic error. */
	PS_BADPID,	/* Bad process handle.  */
	PS_BADLID,	/* Bad LWP identifier.  */
	PS_BADADDR,	/* Bad address.  */
	PS_NOSYM,	/* Could not find given symbol.  */
	PS_NOFREGS	/* FPU register set not available for given LWP.  */
} ps_err_e;

class ThreadDBImpl;

extern "C" {
	#include <thread_db.h>

	ps_err_e ps_pglobal_lookup (ThreadDBImpl *, const char *, const char *, psaddr_t *);
	ps_err_e ps_pdread (ThreadDBImpl *, psaddr_t, void *, size_t);
	ps_err_e ps_pdwrite (ThreadDBImpl *, psaddr_t, const void *, size_t);
	pid_t ps_getpid (ThreadDBImpl *);
}

class ThreadDBImpl : ThreadDB {
protected:
	ThreadDBImpl (MdbInferior *inferior, int pid)
	{
		this->inferior = inferior;
		this->pid = pid;
	}

	bool GetAllThreads (MdbInferior *inferior, ThreadDBCallback *callback);

	ps_err_e GlobalLookup (const char *object_name, const char *sym_name, psaddr_t *addr);
	ps_err_e ReadMemory (psaddr_t address, void *buffer, guint32 size);
	ps_err_e WriteMemory (psaddr_t address, const void *buffer, guint32 size);

private:
	MdbInferior *inferior;
	int pid;

	td_thragent_t *thread_agent;

	friend ps_err_e ps_pglobal_lookup (ThreadDBImpl *, const char *, const char *, psaddr_t *);
	friend ps_err_e ps_pdread (ThreadDBImpl *, psaddr_t, void *, size_t);
	friend ps_err_e ps_pdwrite (ThreadDBImpl *, psaddr_t, const void *, size_t);
	friend pid_t ps_getpid (ThreadDBImpl *);

	friend class ThreadDB;
};

ThreadDB *
ThreadDB::Initialize (MdbInferior *inferior, int pid)
{
	ThreadDBImpl *handle;
	td_err_e e;

	e = td_init ();
	g_message (G_STRLOC ": %d", e);
	if (e)
		return NULL;

	handle = new ThreadDBImpl (inferior, pid);

	e = td_ta_new ((ps_prochandle *) handle, &handle->thread_agent);
	g_message (G_STRLOC ": %d", e);
	if (e) {
		delete handle;
		return NULL;
	}

	return handle;
}

ps_err_e
ThreadDBImpl::GlobalLookup (const char *object_name, const char *sym_name, psaddr_t *addr)
{
	MdbExeReader *reader;
	guint64 symbol;

	g_message (G_STRLOC ": GlobalLookup(%s,%s)", object_name, sym_name);

	reader = inferior->GetProcess()->LookupDll (object_name, false);

	g_message (G_STRLOC ": GlobalLookup(%s,%s) - %p", object_name, sym_name, reader);

	if (!reader)
		return PS_ERR;

	symbol = reader->LookupSymbol (sym_name);

	*addr = GUINT_TO_POINTER (symbol);
	g_message (G_STRLOC ": GlobalLookup(%s,%s) - %p", object_name, sym_name, *addr);
	return PS_OK;
}

ps_err_e
ThreadDBImpl::ReadMemory (psaddr_t address, void *buffer, guint32 size)
{
	ErrorCode result;

	g_message (G_STRLOC ": ReadMemory(%p,%x)", address, size);
	result = inferior->ReadMemory ((gsize) address, size, buffer);
	g_message (G_STRLOC ": ReadMemory(%p,%x) - %d", address, size, result);

	if (result)
		return PS_ERR;

	return PS_OK;
}

ps_err_e
ThreadDBImpl::WriteMemory (psaddr_t address, const void *buffer, guint32 size)
{
	g_message (G_STRLOC ": WriteMemory(%p)", address);
	return PS_ERR;
}

typedef struct {
	ThreadDB *thread_db;
	ThreadDBCallback *callback;
} IterateOverThreadsData;

static int
iterate_over_threads_cb (const td_thrhandle_t *th, void *user_data)
{
	IterateOverThreadsData *data = (IterateOverThreadsData *) user_data;
	td_thrinfo_t ti;
	td_err_e e;

	e = td_thr_get_info (th, &ti);
	if (e)
		return 1;

	g_message (G_STRLOC ": %d - %Lx - %Lx", ti.ti_lid, ti.ti_tid, ti.ti_tls);

	data->callback->Invoke (data->thread_db, ti.ti_lid, ti.ti_tid);

	return 0;
}

bool
ThreadDBImpl::GetAllThreads (MdbInferior *inferior, ThreadDBCallback *callback)
{
	IterateOverThreadsData data;
	td_thrhandle_t th;
	td_err_e e;

	data.thread_db = this;
	data.callback = callback;

	e = td_ta_thr_iter (thread_agent, iterate_over_threads_cb, &data,
			    TD_THR_ANY_STATE, TD_THR_LOWEST_PRIORITY, TD_SIGNO_MASK,
			    TD_THR_ANY_USER_FLAGS);

	return e == 0;
}

extern "C" {
	ps_err_e
	ps_pglobal_lookup (ThreadDBImpl *handle, const char *object_name,
			   const char *sym_name, psaddr_t *sym_addr)
	{
		return handle->GlobalLookup (object_name, sym_name, sym_addr);
	}

	ps_err_e
	ps_pdread (ThreadDBImpl *handle, psaddr_t addr, void *buffer, size_t size)
	{
		return handle->ReadMemory (addr, buffer, size);
	}

	ps_err_e
	ps_pdwrite (ThreadDBImpl *handle, psaddr_t addr, const void *buffer, size_t size)
	{
		return handle->WriteMemory (addr, buffer, size);
	}

	ps_err_e
	ps_lgetregs (ThreadDBImpl *handle, lwpid_t lwp, prgregset_t regs)
	{
		return PS_ERR;
	}

	ps_err_e
	ps_lsetregs (ThreadDBImpl *handle, lwpid_t lwp, const prgregset_t regs)
	{
		return PS_ERR;
	}

	ps_err_e
	ps_lgetfpregs (ThreadDBImpl *handle, lwpid_t lwp, prfpregset_t *regs)
	{
		return PS_ERR;
	}

	ps_err_e
	ps_lsetfpregs (ThreadDBImpl *handle, lwpid_t lwp, const prfpregset_t *regs)
	{
		return PS_ERR;
	}

	pid_t
	ps_getpid (ThreadDBImpl *handle)
	{
		return handle->pid;
	}
}
