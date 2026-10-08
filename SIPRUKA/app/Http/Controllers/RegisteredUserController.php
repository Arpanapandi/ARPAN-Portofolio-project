<?php

namespace App\Http\Controllers\Auth;

use App\Http\Controllers\Controller;
use App\Models\User;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Hash;
use Illuminate\Validation\Rules;

class RegisteredUserController extends Controller
{
    /**
     * Handle an incoming registration request.
     */
    public function store(Request $request)
    {
        // Validasi input register
        $request->validate([
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'string', 'email', 'max:255', 'unique:users'],
            'password' => ['required', 'confirmed', Rules\Password::defaults()],
        ]);

        // Buat user baru dengan role 'user'
        $user = User::create([
            'name' => $request->name,        // wajib
            'email' => $request->email,      // wajib
            'password' => Hash::make($request->password), // wajib
            'role' => 'user',                // wajib kalau kolom role NOT NULL
        ]);

        // Login otomatis
        auth()->login($user);

        // Redirect ke dashboard user
        return redirect()->route('user.dashboard');
    }
}
