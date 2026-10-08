<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl text-gray-800 leading-tight">
            Peminjaman Ruangan
        </h2>
    </x-slot>

    <div class="py-12">
        <div class="max-w-7xl mx-auto sm:px-6 lg:px-8">

            {{-- Form Tambah Peminjaman Ruangan --}}
            <div class="bg-white shadow sm:rounded-lg p-6 mb-6">
                <h3 class="font-semibold text-lg mb-4">Tambah Peminjaman Ruangan</h3>

                <form action="{{ route('peminjaman-ruangan.store') }}" method="POST">
                    @csrf
                    <div class="grid grid-cols-3 gap-4">
                        <select name="ruangan_id" class="border rounded px-3 py-2" required>
                            <option value="">Pilih Ruangan</option>
                            @foreach($ruangan as $r)
                                <option value="{{ $r->id }}">{{ $r->nama }}</option>
                            @endforeach
                        </select>

                        <input type="text" name="peminjam" placeholder="Nama Peminjam"
                               class="border rounded px-3 py-2" required>

                        <input type="date" name="tanggal_pinjam"
                               class="border rounded px-3 py-2" required>

                        <button type="submit"
                                class="col-span-3 md:col-span-1 px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700">
                            Tambah
                        </button>
                    </div>
                </form>
            </div>

            {{-- List Peminjaman Ruangan --}}
            <div class="bg-white shadow sm:rounded-lg">
                <div class="p-6">
                    <h3 class="font-semibold text-lg mb-4">List Peminjaman Ruangan</h3>

                    <table class="table-auto w-full border">
                        <thead class="bg-gray-100 text-gray-700">
                        <tr>
                            <th class="px-4 py-2 text-center w-16">No</th>
                            <th class="px-4 py-2">Ruangan</th>
                            <th class="px-4 py-2">Peminjam</th>
                            <th class="px-4 py-2">Tanggal Pinjam</th>
                            <th class="px-4 py-2 text-center w-40">Aksi</th>
                        </tr>
                        </thead>

                        <tbody>
                        @foreach ($peminjaman as $p)
                            <tr>
                                <td class="border px-4 py-2 text-center">{{ $loop->iteration }}</td>
                                <td class="border px-4 py-2">{{ $p->ruangan->nama }}</td>
                                <td class="border px-4 py-2">{{ $p->peminjam }}</td>
                                <td class="border px-4 py-2">{{ $p->tanggal_pinjam }}</td>

                                <td class="border px-4 py-2 text-center">
                                    <a href="{{ route('peminjaman-ruangan.edit', $p->id) }}"
                                       class="px-3 py-1 bg-yellow-500 text-white rounded hover:bg-yellow-600">Edit</a>

                                    <form action="{{ route('peminjaman-ruangan.destroy', $p->id) }}"
                                          method="POST" class="inline-block"
                                          onsubmit="return confirm('Hapus data ini?')">
                                        @csrf
                                        @method('DELETE')
                                        <button type="submit"
                                                class="px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700">Delete
                                        </button>
                                    </form>
                                </td>
                            </tr>
                        @endforeach
                        </tbody>
                    </table>

                </div>
            </div>

        </div>
    </div>
</x-app-layout>
