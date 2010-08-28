#include <server-object.h>
#include <debugger-mutex.h>

int ServerObject::next_iid = 0;
GHashTable *ServerObject::object_hash = NULL;
DebuggerMutex *ServerObject::lock_mutex = NULL;

void
ServerObject::Initialize (void)
{
	object_hash = g_hash_table_new (NULL, NULL);
	lock_mutex = debugger_mutex_new ();
}

void
ServerObject::Lock ()
{
	debugger_mutex_lock (lock_mutex);
}

void
ServerObject::Unlock ()
{
	debugger_mutex_unlock (lock_mutex);
}

ServerObject *
ServerObject::GetObjectByID (int id)
{
	ServerObject *obj;

	Lock ();
	obj = (ServerObject *) g_hash_table_lookup (object_hash, GUINT_TO_POINTER (id));
	Unlock ();

	return obj;
}

ServerObject *
ServerObject::GetObjectByID (int id, ServerObjectKind kind)
{
	ServerObject *obj = GetObjectByID (id);

	if (!obj || obj->kind != kind)
		return NULL;

	return obj;
}

ServerObject::ServerObject (ServerObjectKind kind)
{
	this->kind = kind;
	this->iid = ++next_iid;

	Lock ();
	g_hash_table_insert (object_hash, GUINT_TO_POINTER (this->iid), this);
	Unlock ();
}

ServerObject::~ServerObject (void)
{
	Lock ();
	g_hash_table_remove (object_hash, GUINT_TO_POINTER (this->iid));
	Unlock ();
}
