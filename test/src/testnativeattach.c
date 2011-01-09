#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

int
main (void)
{
	printf ("testnativeattach: %d\n", getpid ());

	for (;;) {
		printf ("Hello World!\n");
		fflush (stdout);
		sleep (20);
		printf ("Done waiting!\n");
		fflush (stdout);
	}

	return 0;
}
