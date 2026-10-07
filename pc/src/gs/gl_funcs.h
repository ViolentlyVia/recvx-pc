/* PC port: OpenGL 3.3 core entry points used by the renderer, loaded through SDL. */
#pragma once

#include <SDL.h>
#include <SDL_opengl.h>

#ifndef GL_FRAMEBUFFER
#define GL_FRAMEBUFFER 0x8D40
#define GL_READ_FRAMEBUFFER 0x8CA8
#define GL_DRAW_FRAMEBUFFER 0x8CA9
#define GL_COLOR_ATTACHMENT0 0x8CE0
#define GL_DEPTH_ATTACHMENT 0x8D00
#define GL_FRAMEBUFFER_COMPLETE 0x8CD5
#endif
#ifndef GL_ARRAY_BUFFER
#define GL_ARRAY_BUFFER 0x8892
#define GL_STREAM_DRAW 0x88E0
#endif
#ifndef GL_FRAGMENT_SHADER
#define GL_FRAGMENT_SHADER 0x8B30
#define GL_VERTEX_SHADER 0x8B31
#define GL_COMPILE_STATUS 0x8B81
#define GL_LINK_STATUS 0x8B82
#define GL_INFO_LOG_LENGTH 0x8B84
#endif
#ifndef GL_DEPTH_COMPONENT24
#define GL_DEPTH_COMPONENT24 0x81A6
#endif
#ifndef GL_CLAMP_TO_EDGE
#define GL_CLAMP_TO_EDGE 0x812F
#endif
#ifndef GL_TEXTURE0
#define GL_TEXTURE0 0x84C0
#endif
#ifndef GL_FUNC_ADD
#define GL_FUNC_ADD 0x8006
#define GL_FUNC_SUBTRACT 0x800A
#define GL_FUNC_REVERSE_SUBTRACT 0x800B
#endif
#ifndef GL_CONSTANT_ALPHA
#define GL_CONSTANT_ALPHA 0x8003
#define GL_ONE_MINUS_CONSTANT_ALPHA 0x8004
#endif
#ifndef GL_RGBA8
#define GL_RGBA8 0x8058
#endif

#ifndef GL_NUM_EXTENSIONS
#define GL_NUM_EXTENSIONS 0x821D
#endif
#ifndef GL_TEXTURE_MAX_LEVEL
#define GL_TEXTURE_MAX_LEVEL 0x813D
#endif
#define PC_GL_COMPRESSED_RGBA_S3TC_DXT1 0x83F1
#define PC_GL_COMPRESSED_RGBA_S3TC_DXT3 0x83F2
#define PC_GL_COMPRESSED_RGBA_S3TC_DXT5 0x83F3
#define PC_GL_COMPRESSED_RGBA_BPTC_UNORM 0x8E8C

typedef char GLchar;
typedef ptrdiff_t GLsizeiptr;
typedef ptrdiff_t GLintptr;

#define PC_GL_FUNCS(X) \
    X(void, glGenTextures, (GLsizei n, GLuint* t)) \
    X(void, glDeleteTextures, (GLsizei n, const GLuint* t)) \
    X(void, glBindTexture, (GLenum target, GLuint t)) \
    X(void, glTexImage2D, (GLenum target, GLint level, GLint ifmt, GLsizei w, GLsizei h, GLint border, GLenum fmt, GLenum type, const void* data)) \
    X(void, glTexSubImage2D, (GLenum target, GLint level, GLint x, GLint y, GLsizei w, GLsizei h, GLenum fmt, GLenum type, const void* data)) \
    X(void, glTexParameteri, (GLenum target, GLenum pname, GLint param)) \
    X(void, glActiveTexture, (GLenum unit)) \
    X(void, glGenFramebuffers, (GLsizei n, GLuint* f)) \
    X(void, glDeleteFramebuffers, (GLsizei n, const GLuint* f)) \
    X(void, glBindFramebuffer, (GLenum target, GLuint f)) \
    X(void, glFramebufferTexture2D, (GLenum target, GLenum att, GLenum textarget, GLuint t, GLint level)) \
    X(GLenum, glCheckFramebufferStatus, (GLenum target)) \
    X(void, glBlitFramebuffer, (GLint sx0, GLint sy0, GLint sx1, GLint sy1, GLint dx0, GLint dy0, GLint dx1, GLint dy1, GLbitfield mask, GLenum filter)) \
    X(void, glViewport, (GLint x, GLint y, GLsizei w, GLsizei h)) \
    X(void, glScissor, (GLint x, GLint y, GLsizei w, GLsizei h)) \
    X(void, glEnable, (GLenum cap)) \
    X(void, glDisable, (GLenum cap)) \
    X(void, glBlendFuncSeparate, (GLenum sc, GLenum dc, GLenum sa, GLenum da)) \
    X(void, glBlendEquationSeparate, (GLenum c, GLenum a)) \
    X(void, glBlendColor, (GLfloat r, GLfloat g, GLfloat b, GLfloat a)) \
    X(void, glDepthFunc, (GLenum f)) \
    X(void, glDepthMask, (GLboolean m)) \
    X(void, glColorMask, (GLboolean r, GLboolean g, GLboolean b, GLboolean a)) \
    X(void, glClear, (GLbitfield m)) \
    X(void, glClearColor, (GLfloat r, GLfloat g, GLfloat b, GLfloat a)) \
    X(void, glClearDepth, (GLdouble d)) \
    X(GLuint, glCreateShader, (GLenum type)) \
    X(void, glShaderSource, (GLuint s, GLsizei n, const GLchar* const* src, const GLint* len)) \
    X(void, glCompileShader, (GLuint s)) \
    X(void, glGetShaderiv, (GLuint s, GLenum p, GLint* v)) \
    X(void, glGetShaderInfoLog, (GLuint s, GLsizei max, GLsizei* len, GLchar* log)) \
    X(GLuint, glCreateProgram, (void)) \
    X(void, glAttachShader, (GLuint p, GLuint s)) \
    X(void, glBindAttribLocation, (GLuint p, GLuint i, const GLchar* name)) \
    X(void, glLinkProgram, (GLuint p)) \
    X(void, glGetProgramiv, (GLuint p, GLenum n, GLint* v)) \
    X(void, glGetProgramInfoLog, (GLuint p, GLsizei max, GLsizei* len, GLchar* log)) \
    X(void, glUseProgram, (GLuint p)) \
    X(GLint, glGetUniformLocation, (GLuint p, const GLchar* name)) \
    X(void, glUniform1i, (GLint l, GLint v)) \
    X(void, glUniform1f, (GLint l, GLfloat v)) \
    X(void, glUniform2f, (GLint l, GLfloat a, GLfloat b)) \
    X(void, glUniform3f, (GLint l, GLfloat a, GLfloat b, GLfloat c)) \
    X(void, glUniform4f, (GLint l, GLfloat a, GLfloat b, GLfloat c, GLfloat d)) \
    X(void, glUniform2i, (GLint l, GLint a, GLint b)) \
    X(void, glUniform4i, (GLint l, GLint a, GLint b, GLint c, GLint d)) \
    X(void, glGenVertexArrays, (GLsizei n, GLuint* a)) \
    X(void, glBindVertexArray, (GLuint a)) \
    X(void, glGenBuffers, (GLsizei n, GLuint* b)) \
    X(void, glBindBuffer, (GLenum t, GLuint b)) \
    X(void, glBufferData, (GLenum t, GLsizeiptr size, const void* data, GLenum usage)) \
    X(void, glVertexAttribPointer, (GLuint i, GLint size, GLenum type, GLboolean norm, GLsizei stride, const void* p)) \
    X(void, glEnableVertexAttribArray, (GLuint i)) \
    X(void, glDrawArrays, (GLenum mode, GLint first, GLsizei count)) \
    X(void, glReadPixels, (GLint x, GLint y, GLsizei w, GLsizei h, GLenum fmt, GLenum type, void* data)) \
    X(void, glPixelStorei, (GLenum p, GLint v)) \
    X(GLenum, glGetError, (void))     X(void, glCompressedTexImage2D, (GLenum target, GLint level, GLenum ifmt, GLsizei w, GLsizei h, GLint border, GLsizei size, const void* data))     X(const GLubyte*, glGetStringi, (GLenum name, GLuint index))     X(void, glGetIntegerv, (GLenum pname, GLint* data))

#define PC_GL_DECLARE(ret, name, args) typedef ret (APIENTRY* PFN_pc_##name) args; extern PFN_pc_##name pc_##name;
PC_GL_FUNCS(PC_GL_DECLARE)
#undef PC_GL_DECLARE

/* call through the loaded pointers */
#define glGenTextures pc_glGenTextures
#define glDeleteTextures pc_glDeleteTextures
#define glBindTexture pc_glBindTexture
#define glTexImage2D pc_glTexImage2D
#define glTexSubImage2D pc_glTexSubImage2D
#define glTexParameteri pc_glTexParameteri
#define glActiveTexture pc_glActiveTexture
#define glGenFramebuffers pc_glGenFramebuffers
#define glDeleteFramebuffers pc_glDeleteFramebuffers
#define glBindFramebuffer pc_glBindFramebuffer
#define glFramebufferTexture2D pc_glFramebufferTexture2D
#define glCheckFramebufferStatus pc_glCheckFramebufferStatus
#define glBlitFramebuffer pc_glBlitFramebuffer
#define glViewport pc_glViewport
#define glScissor pc_glScissor
#define glEnable pc_glEnable
#define glDisable pc_glDisable
#define glBlendFuncSeparate pc_glBlendFuncSeparate
#define glBlendEquationSeparate pc_glBlendEquationSeparate
#define glBlendColor pc_glBlendColor
#define glDepthFunc pc_glDepthFunc
#define glDepthMask pc_glDepthMask
#define glColorMask pc_glColorMask
#define glClear pc_glClear
#define glClearColor pc_glClearColor
#define glClearDepth pc_glClearDepth
#define glCreateShader pc_glCreateShader
#define glShaderSource pc_glShaderSource
#define glCompileShader pc_glCompileShader
#define glGetShaderiv pc_glGetShaderiv
#define glGetShaderInfoLog pc_glGetShaderInfoLog
#define glCreateProgram pc_glCreateProgram
#define glAttachShader pc_glAttachShader
#define glBindAttribLocation pc_glBindAttribLocation
#define glLinkProgram pc_glLinkProgram
#define glGetProgramiv pc_glGetProgramiv
#define glGetProgramInfoLog pc_glGetProgramInfoLog
#define glUseProgram pc_glUseProgram
#define glGetUniformLocation pc_glGetUniformLocation
#define glUniform1i pc_glUniform1i
#define glUniform1f pc_glUniform1f
#define glUniform2f pc_glUniform2f
#define glUniform3f pc_glUniform3f
#define glUniform4f pc_glUniform4f
#define glUniform2i pc_glUniform2i
#define glUniform4i pc_glUniform4i
#define glGenVertexArrays pc_glGenVertexArrays
#define glBindVertexArray pc_glBindVertexArray
#define glGenBuffers pc_glGenBuffers
#define glBindBuffer pc_glBindBuffer
#define glBufferData pc_glBufferData
#define glVertexAttribPointer pc_glVertexAttribPointer
#define glEnableVertexAttribArray pc_glEnableVertexAttribArray
#define glDrawArrays pc_glDrawArrays
#define glReadPixels pc_glReadPixels
#define glPixelStorei pc_glPixelStorei
#define glGetError pc_glGetError
#define glCompressedTexImage2D pc_glCompressedTexImage2D
#define glGetStringi pc_glGetStringi
#define glGetIntegerv pc_glGetIntegerv

int pcGlLoad(void);
