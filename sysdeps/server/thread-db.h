#ifndef __MONO_DEBUGGER_THREAD_DB_H__
#define __MONO_DEBUGGER_THREAD_DB_H__

#include <mdb-inferior.h>

class ThreadDB;

class ThreadDBCallback {
public:
	void Invoke (ThreadDB *thread_db, int lwp, gsize tid)
	{
		func (thread_db, lwp, tid, user_data);
	}

	ThreadDBCallback (void (*func) (ThreadDB *, int, gsize, gpointer), gpointer user_data)
	{
		this->func = func;
		this->user_data = user_data;
	}
protected:
	gpointer user_data;
	void (* func) (ThreadDB *, int, gsize, gpointer);
};

class ThreadDB {
public:
	static ThreadDB *Initialize (MdbInferior *inferior, int pid);

	virtual bool GetAllThreads (MdbInferior *inferior, ThreadDBCallback *callback) = 0;
};

#endif
