/*
 * xbim_error.h
 *
 * Internal helpers for setting thread-local error messages.
 * Not part of the public C API – used by native implementation code only.
 */

#ifndef XBIM_ERROR_H
#define XBIM_ERROR_H

#ifdef __cplusplus
extern "C" {
#endif

/* Set the thread-local error message (pass NULL to clear). */
void xbim_set_error(const char* msg);

/* Clear the thread-local error message. */
void xbim_clear_error(void);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_ERROR_H */
